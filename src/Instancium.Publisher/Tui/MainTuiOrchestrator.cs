namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using Instancium.Publisher.Tui.Components;
using Microsoft.Extensions.Options;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class MainTuiOrchestrator
{
    private const string ExitMenuLabel = "Exit";
    private readonly IEnumerable<ITuiWorkspace> _workspaces;
    private readonly IGitService _gitService;
    private readonly PublisherSettings _settings;
    private readonly ProjectContext _projectContext;
    private readonly IEnvironmentInfoProvider _infoProvider;

    private record MenuChoice(string? Key, string Display);

    public MainTuiOrchestrator(
        IEnumerable<ITuiWorkspace> workspaces,
        IGitService gitService,
        IOptions<PublisherSettings> settings,
        ProjectContext projectContext,
        IEnvironmentInfoProvider infoProvider)
    {
        _workspaces = workspaces;
        _gitService = gitService;
        _settings = settings.Value;
        _infoProvider = infoProvider;
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

    private static void RenderFatalConfigurationError(string reason, string hint)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold blue]Instancium.Publisher CLI[/]").LeftJustified());
        AnsiConsole.MarkupLine($"[bold red]Critical Error:[/] {Markup.Escape(reason)}");
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(hint)}[/]");
    }

    private void RenderAppHero()
    {
        string version = _infoProvider.GetApplicationVersion();
        string modelId = _settings.AiModelId ?? "Unknown Model";

        AnsiConsole.Write(
            new FigletText("Publisher")
                .LeftJustified()
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine($"[bold blue]Instancium Publisher CLI[/] [dim]v{Markup.Escape(version)}[/]");
        AnsiConsole.MarkupLine("[grey]AI-Powered Release & Commit Automation Pipeline[/]\n");

        var grid = new Grid()
            .AddColumn(new GridColumn().PadRight(2))
            .AddColumn(new GridColumn());

        grid.AddRow("[dim]Active AI Engine:[/]", $"[cyan]{Markup.Escape(modelId)}[/]");
        grid.AddRow("[dim]Loaded Projects:[/]", $"[white]{_settings.RegisteredProjects.Count}[/]");
        grid.AddRow("[dim]Gateway URL:[/]", $"[dim]{Markup.Escape(_settings.AiGatewayUrl ?? "N/A")}[/]");

        AnsiConsole.Write(
            new Panel(grid)
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey)
                .Padding(2, 1)
                .Expand());
    }

    private string? PromptProjectSelection()
    {
        AnsiConsole.Clear();
        RenderAppHero();

        AnsiConsole.WriteLine();
        NavigationBreadcrumbsBar.Render("Projects", "Select Target Repository Context");

        var formattedProjects = TwoColumnMenuFormatter.FormatRows(
            _settings.RegisteredProjects.ToList(),
            kvp => kvp.Key,
            kvp => kvp.Value);

        var choices = formattedProjects
            .Select(kvp => new MenuChoice(kvp.Key.Key, kvp.Value))
            .ToList();

        choices.Add(new MenuChoice(null, "[grey]Exit Application[/]"));

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .PageSize(10)
                .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                .MoreChoicesText("[grey](Move up and down to reveal more projects)[/]")
                .AddChoices(choices)
                .UseConverter(c => c.Display));

        return selected.Key;
    }

    private void ApplyProjectContext(string projectChoice)
    {
        _projectContext.Name = projectChoice;
        string targetPath = _settings.RegisteredProjects[projectChoice];
        _projectContext.WorkingDirectory = targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(targetPath) ?? string.Empty
            : targetPath;

        Environment.CurrentDirectory = _projectContext.WorkingDirectory;
    }

    private async Task<bool> RunWorkspaceSelectionCycleAsync(CancellationToken ct)
    {
        AnsiConsole.Clear();
        string version = _infoProvider.GetApplicationVersion();
        AnsiConsole.Write(new Rule($"[bold cyan]Instancium.Publisher[/] [dim]v{Markup.Escape(version)}[/]").LeftJustified().RuleStyle("grey"));

        await RenderDashboardAsync(ct);

        AnsiConsole.WriteLine();
        NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Select Action");

        var workspaceList = _workspaces.ToList();
        var formattedWorkspaces = TwoColumnMenuFormatter.FormatRows(
            workspaceList,
            w => w.Name,
            w => w.Description);

        var choices = formattedWorkspaces
            .Select(kvp => new MenuChoice(kvp.Key.Name, kvp.Value))
            .ToList();

        choices.Add(new MenuChoice(null, "[grey]Return to Project Selection[/]"));

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<MenuChoice>()
                .PageSize(8)
                .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                .AddChoices(choices)
                .UseConverter(c => c.Display));

        if (selected.Key == null)
        {
            return false;
        }

        var activeWorkspace = _workspaces.First(w => w.Name == selected.Key);
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule($"[cyan]{Markup.Escape(activeWorkspace.Name)}[/]").LeftJustified().RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(activeWorkspace.Description)}[/]\n");

        await activeWorkspace.ExecuteAsync(ct);
        return true;
    }

    private async Task RenderDashboardAsync(CancellationToken ct)
    {
        try
        {
            string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
            bool hasChanges = await _gitService.HasUncommittedChangesAsync(ct);
            bool hasUnpushed = await _gitService.HasUnpushedCommitsAsync(ct);

            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).Expand();
            table.AddColumn(new TableColumn("[silver]Environment Context[/]"));
            table.AddColumn(new TableColumn("[silver]Status[/]"));

            table.AddRow("[dim]Target Repository[/]", $"[bold white]{Markup.Escape(_projectContext.Name)}[/]");
            table.AddRow("[dim]Working Directory[/]", $"[dim]{Markup.Escape(_projectContext.WorkingDirectory)}[/]");
            table.AddRow("[dim]Active Branch[/]", $"[bold cyan]{Markup.Escape(currentBranch)}[/]");

            string pendingChanges = !hasChanges
                ? "[green]Clean[/]"
                : "[yellow]Uncommitted modifications[/]";
            string unpushed = hasUnpushed ? " [dim red](Unpushed commits)[/]" : "";

            table.AddRow("[dim]Git Status[/]", pendingChanges + unpushed);

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to load Git status:[/] {Markup.Escape(ex.Message)}");
            AnsiConsole.MarkupLine("[dim yellow]Ensure the selected directory is a valid git repository.[/]");
        }
    }
}