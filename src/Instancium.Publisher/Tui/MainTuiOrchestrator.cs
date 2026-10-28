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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

public sealed class MainTuiOrchestrator
{
    private const string ExitMenuLabel = "Exit";
    private readonly IEnumerable<ITuiWorkspace> _workspaces;
    private readonly IGitService _gitService;
    private readonly PublisherSettings _settings;
    private readonly ProjectContext _projectContext;

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

    private void RenderHeader()
    {
        string version = GetAppVersion();
        AnsiConsole.Write(new Rule($"[bold blue]Instancium.Publisher CLI[/] [dim]v{Markup.Escape(version)}[/]").LeftJustified());
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }
        var nameVersion = assembly.GetName().Version;
        return nameVersion != null
            ? $"{nameVersion.Major}.{nameVersion.Minor}.{nameVersion.Build}"
            : "0.0.0";
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
        return selected == ExitMenuLabel ? null : selected;
    }

    /// <summary>
    /// Applies the selected repository context. The path is always treated as a repository root
    /// (repository-scoped versioning), regardless of whether the appsettings entry points to a
    /// .csproj file or a directory.
    /// </summary>
    private void ApplyProjectContext(string projectChoice)
    {
        _projectContext.Name = projectChoice;
        string targetPath = _settings.RegisteredProjects[projectChoice];

        // Normalize: if the registered path points at a .csproj, use its parent directory as the
        // repository root. Versioning is repository-scoped, not per-project.
        _projectContext.WorkingDirectory = targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(targetPath) ?? string.Empty
            : targetPath;

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

    private async Task RenderDashboardAsync(CancellationToken ct)
    {
        try
        {
            string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
            bool hasChanges = await _gitService.HasUncommittedChangesAsync(ct);
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Expand();
            table.AddColumn(new TableColumn("[silver]System Metric[/]"));
            table.AddColumn(new TableColumn("[silver]Current Status[/]"));
            table.AddRow("Active Repository", $"[bold white]{Markup.Escape(_projectContext.Name)}[/]");
            table.AddRow("Repository Root", $"[dim]{Markup.Escape(_projectContext.WorkingDirectory)}[/]");
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