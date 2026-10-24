namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Orchestrates hierarchical CLI navigation: project selection followed by workspace selection.
/// </summary>
public sealed class MainTuiOrchestrator
{
    private const string ExitMenuLabel = "Exit";

    private readonly IEnumerable<ITuiWorkspace> _workspaces;
    private readonly IGitService _gitService;
    private readonly PublisherSettings _settings;
    private readonly ProjectContext _projectContext;

    /// <summary>
    /// Initializes the orchestrator with the registered workspaces and application services.
    /// </summary>
    /// <param name="workspaces">All workspace implementations resolved from the DI container.</param>
    /// <param name="gitService">Git operations facade used by the dashboard.</param>
    /// <param name="settings">Publisher configuration bound from appsettings.json.</param>
    /// <param name="projectContext">Mutable context describing the currently selected repository.</param>
    public MainTuiOrchestrator(
        IEnumerable<ITuiWorkspace> workspaces,
        IGitService gitService,
        IOptions<PublisherSettings> settings,
        ProjectContext projectContext)
    {
        _workspaces = workspaces;
        _gitService = gitService;
        _settings = settings.Value;
        _projectContext = projectContext;
    }

    /// <summary>
    /// Runs the interactive navigation loop until the user exits from the top-level menu
    /// or the host cancellation token is triggered.
    /// </summary>
    /// <param name="ct">Cancellation token propagated to workspaces and git operations.</param>
    public async Task RunInteractiveLoopAsync(CancellationToken ct = default)
    {
        if (!TryValidateNavigationConfiguration())
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            string? projectChoice = PromptProjectSelection();
            if (projectChoice == null)
            {
                break;
            }

            ApplyProjectContext(projectChoice);

            while (!ct.IsCancellationRequested)
            {
                bool stayInWorkspaceMenu = await RunWorkspaceSelectionCycleAsync(ct);
                if (!stayInWorkspaceMenu)
                {
                    break;
                }
            }
        }
    }

    private bool TryValidateNavigationConfiguration()
    {
        if (_settings.RegisteredProjects == null || _settings.RegisteredProjects.Count == 0)
        {
            RenderFatalConfigurationError(
                "No projects configured.",
                "Please add a 'Projects' dictionary to your appsettings.json.");
            return false;
        }

        if (_settings.RegisteredProjects.Keys.Contains(ExitMenuLabel, StringComparer.OrdinalIgnoreCase))
        {
            RenderFatalConfigurationError(
                $"Registered project name '{ExitMenuLabel}' is reserved by the navigation menu.",
                "Rename the conflicting project key in appsettings.json.");
            return false;
        }

        if (_workspaces.Any(w => string.Equals(w.Name, ExitMenuLabel, StringComparison.OrdinalIgnoreCase)))
        {
            RenderFatalConfigurationError(
                $"Workspace name '{ExitMenuLabel}' is reserved by the navigation menu.",
                "Rename the conflicting workspace.");
            return false;
        }

        return true;
    }

    private static void RenderFatalConfigurationError(string reason, string hint)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold blue]Instancium.Publisher CLI[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[bold red]Critical Error:[/] {Markup.Escape(reason)}");
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(hint)}[/]");
    }

    private string? PromptProjectSelection()
    {
        AnsiConsole.Clear();
        RenderHeader();

        var choices = _settings.RegisteredProjects.Keys.ToList();
        choices.Add(ExitMenuLabel);

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("\n[cyan]Select Target Repository / Project:[/]")
                .PageSize(10)
                .AddChoices(choices));

        // Selecting 'Exit' at the top level terminates the application.
        return selected == ExitMenuLabel ? null : selected;
    }

    private void ApplyProjectContext(string projectChoice)
    {
        // The configuration key is the human-readable project name displayed on the dashboard.
        _projectContext.Name = projectChoice;

        string targetPath = _settings.RegisteredProjects[projectChoice];
        if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            _projectContext.ProjectFilePath = targetPath;
            _projectContext.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        }
        else
        {
            _projectContext.ProjectFilePath = null;
            _projectContext.WorkingDirectory = targetPath;
        }

        Environment.CurrentDirectory = _projectContext.WorkingDirectory;
    }

    private async Task<bool> RunWorkspaceSelectionCycleAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        RenderHeader();
        await RenderDashboardAsync(ct);

        var choices = _workspaces.ToDictionary(w => w.Name, w => w);
        choices.Add(ExitMenuLabel, null!);

        var selectedName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("\n[cyan]Select Target Workspace:[/]")
                .PageSize(5)
                .AddChoices(choices.Keys));

        // Selecting 'Exit' at this level leaves the workspace menu
        // and returns to the project selection menu instead of terminating the process.
        if (selectedName == ExitMenuLabel)
        {
            return false;
        }

        var activeWorkspace = choices[selectedName];
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[yellow]{activeWorkspace.Name}[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[dim]{activeWorkspace.Description}[/]\n");
        await activeWorkspace.ExecuteAsync(ct);
        return true;
    }

    private void RenderHeader()
    {
        AnsiConsole.Write(new Rule("[bold blue]Instancium.Publisher CLI[/]").LeftJustified());
    }

    private async Task RenderDashboardAsync(CancellationToken ct)
    {
        try
        {
            string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
            bool hasChanges = await _gitService.HasUncommittedChangesAsync(ct);

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Expand();
            table.AddColumn(new TableColumn("[silver]System Metric[/]"));
            table.AddColumn(new TableColumn("[silver]Current Status[/]"));
            table.AddRow("Active Project", $"[bold white]{Markup.Escape(_projectContext.Name)}[/]");
            table.AddRow("Target Path", $"[dim]{Markup.Escape(_projectContext.WorkingDirectory)}[/]");
            table.AddRow("Active Branch", $"[bold cyan]{Markup.Escape(currentBranch)}[/]");
            table.AddRow("Pending Changes", !hasChanges ? "[dim green]Clean[/]" : "[yellow]Uncommitted modifications[/]");
            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load Git status:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim yellow]Ensure the selected directory is a valid git repository.[/]");
        }
    }
}