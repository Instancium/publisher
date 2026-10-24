namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Core.Domain;
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
        IVersionEngine versionEngine)
    {
        _stateManager = stateManager;
        _gitService = gitService;
        _bumpStep = bumpStep;
        _publishStep = publishStep;
        _aiAssistant = aiAssistant;
        _projectContext = projectContext;
        _versionEngine = versionEngine;
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
                activeContext.SourceBranch = await _gitService.GetCurrentBranchAsync(ct);
                string projectPath = GetProjectFilePath();
                var currentVersion = await _versionEngine.ReadVersionFromProjectAsync(projectPath, ct);
                RenderContextPanel(currentVersion);

                var choices = Enum.GetValues<VersionBumpStrategy>()
                    .Select(s => new MenuOption(s))
                    .ToList();
                choices.Add(new MenuOption(null));

                var optionChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<MenuOption>()
                        .Title("\n[cyan]Select version bump strategy (Use arrow keys):[/]")
                        .PageSize(12)
                        .AddChoices(choices)
                        .UseConverter(opt => FormatStrategyWithExample(opt.Strategy)));

                if (optionChoice.Strategy == null)
                {
                    return;
                }

                await AnsiConsole.Status().StartAsync("Bumping version...", async ctx =>
                {
                    await _bumpStep.ExecuteAsync(activeContext, projectPath, optionChoice.Strategy.Value, ct);
                });
                AnsiConsole.MarkupLine($"[green]Version bumped to v{activeContext.NewVersion}[/]");

                activeContext.ReleaseBranch = $"release-prep/v{activeContext.NewVersion}";
                await _gitService.CheckoutBranchAsync(activeContext.ReleaseBranch, createNew: true, ct);
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
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Release Pipeline Faulted:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim]Your progress has been saved. You can resume or rollback later.[/]");
            await _stateManager.SaveAsync(activeContext);
            AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
            Console.ReadKey(intercept: true);
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
            // Headers without surviving entries (all placeholders removed) are omitted entirely.
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

    private void RenderContextPanel(SemanticVersion currentVersion)
    {
        var panel = new Panel(
                $"Active Project:  [bold white]{Markup.Escape(_projectContext.Name)}[/]\n" +
                $"Target Path:     [dim]{Markup.Escape(_projectContext.WorkingDirectory)}[/]\n" +
                $"Current Version: [bold green]{currentVersion}[/]")
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

    private string GetProjectFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_projectContext.ProjectFilePath) && File.Exists(_projectContext.ProjectFilePath))
        {
            return _projectContext.ProjectFilePath;
        }

        var targetDir = new DirectoryInfo(_projectContext.WorkingDirectory);
        var csprojFiles = targetDir.GetFiles("*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (csprojFiles.Count == 0)
        {
            throw new FileNotFoundException($"No .csproj file found under target directory or subdirectories: {targetDir.FullName}");
        }

        if (csprojFiles.Count == 1)
        {
            return csprojFiles[0].FullName;
        }

        var exactMatch = csprojFiles.FirstOrDefault(f =>
            Path.GetFileNameWithoutExtension(f.Name).Equals(targetDir.Name, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            return exactMatch.FullName;
        }

        var chosen = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[cyan]Multiple .csproj files found. Select target project for versioning:[/]")
                .PageSize(10)
                .AddChoices(csprojFiles.Select(f => Path.GetRelativePath(targetDir.FullName, f.FullName))));

        return Path.Combine(targetDir.FullName, chosen);
    }
}