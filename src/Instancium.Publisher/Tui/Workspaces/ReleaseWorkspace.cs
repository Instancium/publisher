namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Exceptions;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Pipeline.Steps;
using Instancium.Publisher.State;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class ReleaseWorkspace : ITuiWorkspace
{
    private readonly IPipelineStateManager _stateManager;
    private readonly IGitService _gitService;
    private readonly BumpVersionStep _bumpStep;
    private readonly MergeAndPublishStep _publishStep;
    private readonly IAiCommitAssistant _aiAssistant;
    private readonly ProjectContext _projectContext;
    private readonly IVersionEngine _versionEngine;
    private readonly IProjectDiscoveryService _projectDiscovery;

    public string Name => "Release Master & Recovery";
    public string Description => "End-to-End release pipeline with AI Changelog and rollback capability";

    private record MenuOption(VersionBumpStrategy? Strategy);

    public ReleaseWorkspace(
        IPipelineStateManager stateManager,
        IGitService gitService,
        BumpVersionStep bumpStep,
        MergeAndPublishStep publishStep,
        IAiCommitAssistant aiAssistant,
        ProjectContext projectContext,
        IVersionEngine versionEngine,
        IProjectDiscoveryService projectDiscovery)
    {
        _stateManager = stateManager;
        _gitService = gitService;
        _bumpStep = bumpStep;
        _publishStep = publishStep;
        _aiAssistant = aiAssistant;
        _projectContext = projectContext;
        _versionEngine = versionEngine;
        _projectDiscovery = projectDiscovery;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var activeContext = await _stateManager.LoadAsync();
        if (activeContext != null && activeContext.CurrentStep > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]Interrupted release detected at step {activeContext.CurrentStep}.[/]");
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Recovery Dispatcher:")
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
            if (activeContext.CurrentStep <= 1)
            {
                string repositoryRoot = await _gitService.GetRepositoryRootAsync(ct);
                IReadOnlyList<string> projectPaths = await _projectDiscovery.DiscoverProjectFilesAsync(repositoryRoot, ct);
                if (projectPaths.Count == 0)
                {
                    throw new FileNotFoundException($"No .csproj project files found under repository root: {repositoryRoot}");
                }
                IReadOnlyList<ProjectVersionSnapshot> snapshots = await _versionEngine.ReadProjectVersionsAsync(projectPaths, ct);
                SemanticVersion? repositoryVersion = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);
                RenderContextPanel(repositoryVersion, snapshots, repositoryRoot);

                VersionBumpStrategy? strategy = null;
                string? customVersion = null;
                if (activeContext.NewVersion is null)
                {
                    (strategy, customVersion) = PromptVersionStrategy();
                    if (strategy is null)
                    {
                        return;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]Resuming interrupted release: planned version v{activeContext.NewVersion} will be applied to all {projectPaths.Count} project(s).[/]");
                }

                if (string.IsNullOrWhiteSpace(activeContext.SourceBranch))
                {
                    activeContext.SourceBranch = await _gitService.GetCurrentBranchAsync(ct);
                }

                await AnsiConsole.Status().StartAsync("Bumping repository version...", async ctx =>
                {
                    await _bumpStep.ExecuteAsync(activeContext, snapshots, strategy, customVersion, ct);
                });
                AnsiConsole.MarkupLine($"[green]Repository version set to v{activeContext.NewVersion} across {projectPaths.Count} project(s).[/]");

                activeContext.ReleaseBranch = $"release-prep/v{activeContext.NewVersion}";
                await CheckoutReleaseBranchAsync(activeContext.ReleaseBranch, ct);
                activeContext.CurrentStep = 2;
                await _stateManager.SaveAsync(activeContext);
            }
            if (activeContext.CurrentStep <= 2)
            {
                AnsiConsole.MarkupLine("\n[green]Generating Release Notes (AI Changelog):[/]");
                AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
                string lastTag = string.Empty;
                try { lastTag = await _gitService.GetLatestTagAsync(ct); } catch { }
                string rangeDescription = string.IsNullOrWhiteSpace(lastTag)
                    ? "last 20 commits (no tags found in repository)"
                    : $"{lastTag}..HEAD";
                AnsiConsole.MarkupLine($"[dim]Changelog commit range: {Markup.Escape(rangeDescription)}[/]");
                string gitLog = await _gitService.GetCommitLogAsync(lastTag, ct);
                var fullNotesBuilder = new StringBuilder();
                await foreach (var token in _aiAssistant.StreamReleaseNotesAsync(gitLog, ct))
                {
                    CliMarkup.Print($"[silver]{Markup.Escape(token)}[/]");
                    fullNotesBuilder.Append(token);
                }
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
                string rawNotes = fullNotesBuilder.ToString().Trim();
                activeContext.ReleaseNotes = SanitizeReleaseNotes(rawNotes);
                var approveChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\nProceed with this release?")
                        .AddChoices("Approve and Publish", "Abort (Keep state)", "Rollback (Destroy branch)"));
                if (approveChoice == "Abort (Keep state)") return;
                if (approveChoice == "Rollback (Destroy branch)")
                {
                    await ExecuteDestructiveRollbackAsync(activeContext, ct);
                    return;
                }
                activeContext.CurrentStep = 3;
                await _stateManager.SaveAsync(activeContext);
            }
            if (activeContext.CurrentStep <= 3)
            {
                await AnsiConsole.Status().StartAsync("Merging and publishing release to GitHub...", async ctx =>
                {
                    await _gitService.AddAllAsync(ct);
                    await _gitService.CommitAsync($"chore(release): prepare version v{activeContext.NewVersion}", ct);
                    await _publishStep.ExecuteAsync(activeContext, "main", ct);
                    await _gitService.CheckoutBranchAsync(activeContext.SourceBranch, false, ct);
                    await _gitService.DeleteLocalBranchAsync(activeContext.ReleaseBranch, ct);
                    await _gitService.DeleteRemoteBranchAsync(activeContext.ReleaseBranch, ct);
                });
                AnsiConsole.MarkupLine($"\n[bold green]Release v{activeContext.NewVersion} published successfully![/]");
                await _stateManager.RollbackAsync();
                AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
                Console.ReadKey(intercept: true);
            }
        }
        catch (ProjectFileParseException ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Release Pipeline Faulted:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Fix the file above: resolve merge conflicts, repair encoding, or restore it via 'git checkout -- <file>'. Then choose Resume.[/]");
            await _stateManager.SaveAsync(activeContext);
            AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
            Console.ReadKey(intercept: true);
        }
    }

    private (VersionBumpStrategy? Strategy, string? CustomVersion) PromptVersionStrategy()
    {
        var choices = Enum.GetValues<VersionBumpStrategy>()
            .Select(s => new MenuOption(s))
            .ToList();
        choices.Add(new MenuOption(null));
        MenuOption optionChoice = AnsiConsole.Prompt(
            new SelectionPrompt<MenuOption>()
                .Title("\n[cyan]Select version bump strategy (Use arrow keys):[/]")
                .PageSize(12)
                .AddChoices(choices)
                .UseConverter(opt => FormatStrategyWithExample(opt.Strategy)));
        if (optionChoice.Strategy is null)
        {
            return (null, null);
        }
        if (optionChoice.Strategy != VersionBumpStrategy.Custom)
        {
            return (optionChoice.Strategy, null);
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
                return (VersionBumpStrategy.Custom, _versionEngine.Parse(rawVersion).ToString());
            }
            catch (FormatException)
            {
                AnsiConsole.MarkupLine($"[red]'{Markup.Escape(rawVersion)}' is not a valid SemVer 2.0 string. Try again.[/]");
            }
        }
    }

    private async Task CheckoutReleaseBranchAsync(string branchName, CancellationToken ct)
    {
        try
        {
            await _gitService.CheckoutBranchAsync(branchName, createNew: true, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine($"[yellow]Release branch '{Markup.Escape(branchName)}' already exists. Reusing it.[/]");
            await _gitService.CheckoutBranchAsync(branchName, createNew: false, ct);
        }
    }

    private static string SanitizeReleaseNotes(string rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes))
        {
            return string.Empty;
        }
        var lines = rawNotes.Replace("\r\n", "\n").Split('\n');
        var rebuilt = new List<string>();
        var headerBlock = new List<string>();
        var sectionEntries = new List<string>();
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (IsBulletLine(line))
            {
                if (!IsPlaceholderBullet(line))
                {
                    sectionEntries.Add(line);
                }
                continue;
            }
            if (sectionEntries.Count > 0)
            {
                FlushSection(rebuilt, headerBlock, sectionEntries);
                headerBlock = new List<string>();
                sectionEntries = new List<string>();
            }
            headerBlock.Add(line);
        }
        FlushSection(rebuilt, headerBlock, sectionEntries);
        return string.Join(Environment.NewLine, rebuilt).Trim();
    }

    private static void FlushSection(List<string> target, List<string> headerBlock, List<string> sectionEntries)
    {
        if (sectionEntries.Count == 0)
        {
            return;
        }
        target.AddRange(headerBlock);
        target.AddRange(sectionEntries);
        target.Add(string.Empty);
    }

    private static bool IsBulletLine(string line)
        => line.StartsWith("- ", StringComparison.Ordinal)
        || line.StartsWith("* ", StringComparison.Ordinal)
        || line.StartsWith("•", StringComparison.Ordinal);

    private static bool IsPlaceholderBullet(string line)
    {
        string text = line.TrimStart('-', '*', '•').Trim().TrimEnd('.').Trim();
        return text.Length == 0
            || text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || text.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || text.Equals("na", StringComparison.OrdinalIgnoreCase)
            || text.Equals("no changes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("nothing", StringComparison.OrdinalIgnoreCase);
    }

    private void RenderContextPanel(
        SemanticVersion? repositoryVersion,
        IReadOnlyList<ProjectVersionSnapshot> snapshots,
        string repositoryRoot)
    {
        int divergentProjects = RepositoryVersionPolicy.CountDivergentProjects(snapshots, repositoryVersion);
        string versionText = repositoryVersion is null
            ? "[yellow]none declared (initial baseline 1.0.0)[/]"
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

    private string FormatStrategyWithExample(VersionBumpStrategy? strategy)
    {
        if (!strategy.HasValue)
        {
            return "Return to Menu          [dim]Exit to Main CLI[/]";
        }
        return strategy.Value switch
        {
            VersionBumpStrategy.Patch => "Patch                   [dim]1.0.0 ➔ 1.0.1[/]",
            VersionBumpStrategy.Minor => "Minor                   [dim]1.0.0 ➔ 1.1.0[/]",
            VersionBumpStrategy.Major => "Major                   [dim]1.0.0 ➔ 2.0.0[/]",
            VersionBumpStrategy.NewPreReleasePatch => "Pre-Release Patch       [dim]1.0.0 ➔ 1.0.1-rc.1[/]",
            VersionBumpStrategy.NewPreReleaseMinor => "Pre-Release Minor       [dim]1.0.0 ➔ 1.1.0-rc.1[/]",
            VersionBumpStrategy.NewPreReleaseMajor => "Pre-Release Major       [dim]1.0.0 ➔ 2.0.0-rc.1[/]",
            VersionBumpStrategy.PreReleaseIteration => "Pre-Release Iteration   [dim]1.0.1-rc.1 ➔ 1.0.1-rc.2[/]",
            VersionBumpStrategy.PromoteToRelease => "Promote To Release      [dim]1.0.1-rc.2 ➔ 1.0.1[/]",
            VersionBumpStrategy.Custom => "Custom                  [dim]User defined[/]",
            _ => strategy.ToString() ?? string.Empty
        };
    }

    private async Task ExecuteDestructiveRollbackAsync(PipelineContext context, CancellationToken ct)
    {
        AnsiConsole.MarkupLine("[red]Executing destructive rollback...[/]");
        try
        {
            await _gitService.RestoreAllAsync(ct);
            if (!string.IsNullOrEmpty(context.SourceBranch))
            {
                await _gitService.CheckoutBranchAsync(context.SourceBranch, false, ct);
            }
            if (!string.IsNullOrEmpty(context.ReleaseBranch))
            {
                await _gitService.DeleteLocalBranchAsync(context.ReleaseBranch, ct);
                await _gitService.DeleteRemoteBranchAsync(context.ReleaseBranch, ct);
            }
            await _stateManager.RollbackAsync();
            AnsiConsole.MarkupLine("[green]Local changes discarded and pipeline state cleared.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Rollback FATAL error:[/] {Spectre.Console.Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[yellow]Pipeline state retained. Manual Git fix may be required before retrying Rollback.[/]");
            throw;
        }
    }
}