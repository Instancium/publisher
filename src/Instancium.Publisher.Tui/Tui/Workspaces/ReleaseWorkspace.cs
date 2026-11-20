namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Application.Workflows;
using Instancium.Publisher.Domain.Exceptions;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Publisher.Tui.Abstractions;
using Instancium.Publisher.Tui.Components;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ReleaseWorkspace(
    ReleaseWorkflow workflow,
    ReleaseRecoveryService recoveryService,
    IVersionBumpService versionBumpService,
    IOptions<PublisherSettings> settings,
    ProjectContext projectContext) : ITuiWorkspace
{
    private readonly ReleaseWorkflow _workflow = workflow;
    private readonly ReleaseRecoveryService _recoveryService = recoveryService;
    private readonly IVersionBumpService _versionBumpService = versionBumpService;
    private readonly PublisherSettings _settings = settings.Value;
    private readonly ProjectContext _projectContext = projectContext;

    public string Name => "Release Master & Recovery";
    public string Description => "End-to-End release pipeline with AI Changelog and rollback capability";

    private record MenuOption(IVersionBumpStrategy? Strategy);
    private record StrategyMenuChoice(IVersionBumpStrategy? Strategy, string Display);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var activeContext = await _workflow.LoadStateAsync();
        if (activeContext != null && activeContext.CurrentStep > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Interrupted release detected at step {activeContext.CurrentStep}.[/]");
            AnsiConsole.WriteLine();
            NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Release", "Recovery Dispatcher");

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                    .AddChoices("Resume", "Rollback", "Start Fresh"));

            if (choice == "Rollback")
            {
                await ExecuteDestructiveRollbackAsync(activeContext, ct);
                return;
            }
            if (choice == "Start Fresh")
            {
                await ExecuteDestructiveRollbackAsync(activeContext, ct);
                activeContext = null;
            }
        }

        activeContext ??= new PipelineContext { CurrentStep = 1 };

        try
        {
            if (activeContext.CurrentStep > 1 && !string.IsNullOrWhiteSpace(activeContext.ReleaseBranch))
            {
                AnsiConsole.MarkupLine($"[dim]Restoring context: checking out {Markup.Escape(activeContext.ReleaseBranch)}...[/]");
                await _workflow.EnsureReleaseBranchCheckedOutAsync(activeContext.ReleaseBranch, ct);
            }

            if (activeContext.CurrentStep <= 1)
            {
                ReleaseRepositoryContext repoContext = await _workflow.InspectRepositoryAsync(ct);

                RenderContextPanel(repoContext.CurrentVersion, repoContext.Snapshots, repoContext.RepositoryRoot);

                string? strategyId = null;
                VersionBumpContext? bumpContext = null;

                if (activeContext.NewVersion is null)
                {
                    string? customVersion;
                    (strategyId, customVersion) = PromptVersionStrategy(repoContext.CurrentVersion);
                    if (strategyId is null)
                    {
                        return;
                    }

                    bumpContext = customVersion is null ? null : new VersionBumpContext(CustomVersion: customVersion);
                    ReleasePlan plan = await _workflow.CreatePlanAsync(
                        repoContext,
                        strategyId,
                        bumpContext,
                        _settings.ReleaseBranchPrefix,
                        ct);

                    activeContext.CurrentVersion = plan.CurrentVersion;
                    activeContext.NewVersion = plan.NextVersion;
                    if (string.IsNullOrWhiteSpace(activeContext.SourceBranch))
                    {
                        activeContext.SourceBranch = plan.SourceBranch;
                    }
                    activeContext.ReleaseBranch = plan.ReleaseBranch;
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]Resuming interrupted release: planned version v{activeContext.NewVersion} will be applied to all {repoContext.Snapshots.Count} project(s).[/]");

                    if (string.IsNullOrWhiteSpace(activeContext.SourceBranch) || string.IsNullOrWhiteSpace(activeContext.ReleaseBranch))
                    {
                        activeContext.ReleaseBranch = $"{_settings.ReleaseBranchPrefix}{activeContext.NewVersion}";
                    }
                }

                await TuiProgressReporter.RunWithLiveStatusAsync(
                    "Bumping repository version...",
                    async () => await _workflow.ApplyVersionAsync(activeContext, repoContext.Snapshots, strategyId, bumpContext, ct),
                    ct);

                AnsiConsole.MarkupLine($"[green]Repository version set to v{activeContext.NewVersion} across {repoContext.Snapshots.Count} project(s).[/]");
            }

            if (activeContext.CurrentStep <= 2)
            {
                ReleaseNotesInput notesInput = await _workflow.PrepareReleaseNotesContextAsync(activeContext, ct);
                AnsiConsole.MarkupLine($"\n[dim]Changelog commit range: {Markup.Escape(notesInput.RangeDescription)}[/]");

                var fullNotesBuilder = new StringBuilder();
                await using var enumerator = _workflow.StreamReleaseNotesAsync(notesInput.GitLog, ct).GetAsyncEnumerator(ct);
                bool hasData = false;

                await TuiProgressReporter.RunWithLiveStatusAsync(
                    "Analyzing commit history...",
                    async () => { hasData = await enumerator.MoveNextAsync(); },
                    ct);

                AnsiConsole.MarkupLine("\n[green]Generating Release Notes (AI Changelog):[/]");

                if (hasData)
                {
                    fullNotesBuilder.Append(enumerator.Current);
                    await AnsiConsole.Live(new Panel("[dim]Initializing stream...[/]"))
                        .AutoClear(false)
                        .StartAsync(async ctx =>
                        {
                            while (true)
                            {
                                string safeText = Markup.Escape(fullNotesBuilder.ToString());
                                ctx.UpdateTarget(
                                    new Panel($"[silver]{safeText}[/]")
                                        .Header("[cyan]Live AI Stream[/]")
                                        .BorderColor(Color.Grey)
                                        .Expand());

                                if (!await enumerator.MoveNextAsync()) break;
                                fullNotesBuilder.Append(enumerator.Current);
                            }

                            ctx.UpdateTarget(MarkdownReleaseNotesRenderer.Render(fullNotesBuilder.ToString()));
                        });
                }

                AnsiConsole.WriteLine();
                activeContext.ReleaseNotes = await _workflow.FinalizeReleaseNotesAsync(fullNotesBuilder.ToString().Trim(), ct);

                AnsiConsole.WriteLine();
                NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Release", "Confirm Publication");

                var approveChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                        .AddChoices("Approve and Publish", "Abort (Keep state)", "Rollback (Destroy branch)"));

                if (approveChoice == "Abort (Keep state)") return;
                if (approveChoice == "Rollback (Destroy branch)")
                {
                    await ExecuteDestructiveRollbackAsync(activeContext, ct);
                    return;
                }

                await _workflow.AdvanceToPublishStepAsync(activeContext);
            }

            if (activeContext.CurrentStep <= 3)
            {
                await TuiProgressReporter.RunWithLiveStatusAsync(
                    "Merging and publishing release to GitHub...",
                    async () => await _workflow.PublishAsync(activeContext, _settings.TargetBranch, ct),
                    ct);

                AnsiConsole.MarkupLine($"\n[bold green]Release v{activeContext.NewVersion} published successfully![/]");
                AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
                Console.ReadKey(intercept: true);
            }
        }
        catch (ProjectFileParseException ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Release Pipeline Faulted:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Fix the file above: resolve merge conflicts, repair encoding, or restore it via 'git checkout -- <file>'. Then choose Resume.[/]");
            await _workflow.SaveStateAsync(activeContext);
            AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
            Console.ReadKey(intercept: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Release Pipeline Faulted:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Pipeline state has been saved. If a merge failed, it was automatically aborted to protect your project files. You can safely choose 'Resume' after investigating the issue.[/]");
            await _workflow.SaveStateAsync(activeContext);
            AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    private (string? StrategyId, string? CustomVersion) PromptVersionStrategy(SemanticVersion? currentVersion)
    {
        AnsiConsole.WriteLine();
        NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Release", "Select Bump Strategy");

        var rawOptions = _versionBumpService.Strategies
            .Select(s => new MenuOption(s))
            .ToList();
        rawOptions.Add(new MenuOption(null));

        SemanticVersion baseline = currentVersion ?? RepositoryVersionPolicy.InitialVersion;

        var formattedLookup = TwoColumnMenuFormatter.FormatRows(
            rawOptions,
            opt => opt.Strategy?.DisplayName ?? "Return to Menu",
            opt =>
            {
                if (opt.Strategy is null)
                {
                    return "Exit to Main CLI";
                }

                if (opt.Strategy.RequiresCustomVersion)
                {
                    return opt.Strategy.Description;
                }

                SemanticVersion next = _versionBumpService.CalculateNext(currentVersion, opt.Strategy.Id);
                return $"{baseline} \u2192 {next}";
            });

        var choices = rawOptions
            .Select(opt => new StrategyMenuChoice(opt.Strategy, formattedLookup[opt]))
            .ToList();

        StrategyMenuChoice selectedChoice = AnsiConsole.Prompt(
            new SelectionPrompt<StrategyMenuChoice>()
                .PageSize(12)
                .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                .AddChoices(choices)
                .UseConverter(opt => opt.Display));

        if (selectedChoice.Strategy is null)
        {
            return (null, null);
        }

        if (!selectedChoice.Strategy.RequiresCustomVersion)
        {
            return (selectedChoice.Strategy.Id, null);
        }

        while (true)
        {
            string rawVersion = AnsiConsole.Prompt(
                new TextPrompt<string>("Enter custom version (SemVer 2.0, empty to cancel):")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return (null, null);
            }

            try
            {
                return (selectedChoice.Strategy.Id, SemanticVersion.Parse(rawVersion).ToString());
            }
            catch (FormatException)
            {
                AnsiConsole.MarkupLine($"[red]'{Markup.Escape(rawVersion)}' is not a valid SemVer 2.0 string. Try again.[/]");
            }
        }
    }

    private void RenderContextPanel(
        SemanticVersion? repositoryVersion,
        IReadOnlyList<ProjectVersionSnapshot> snapshots,
        string repositoryRoot)
    {
        int divergentProjects = RepositoryVersionPolicy.CountDivergentProjects(snapshots, repositoryVersion);
        string versionText = repositoryVersion is null
            ? "[yellow]none declared (initial baseline 1.0.0)[/]
            : $"[bold green]{repositoryVersion}[/]";

        string divergentText = divergentProjects == 0
            ? "[dim green]none[/]"
            : $"[yellow]{divergentProjects} (will be aligned on release)[/]";

        var panel = new Panel(
            $"Active Project:      [bold white]{Markup.Escape(_projectContext.Name)}[/]\n" +
            $"Repository Root:     [dim]{Markup.Escape(repositoryRoot)}[/]\n" +
            $"Projects to Version: [bold white]{snapshots.Count}[/]\n" +
            $"Repository Version:  {versionText}\n" +
            $"Divergent Projects:  {divergentText}")
            .Header("[yellow]Release Context[/]")
            .BorderColor(Color.Grey)
            .RoundedBorder()
            .Expand();

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private async Task ExecuteDestructiveRollbackAsync(PipelineContext context, CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[red]Executing safe rollback...[/]");
        try
        {
            await _recoveryService.ExecuteDestructiveRollbackAsync(context, ct);
            AnsiConsole.MarkupLine("[green]Release branch removed and pipeline state reset safely.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Rollback FATAL error:[/] {Spectre.Console.Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Pipeline state retained. Manual Git fix may be required before retrying Rollback.[/]");
            throw;
        }
    }
}