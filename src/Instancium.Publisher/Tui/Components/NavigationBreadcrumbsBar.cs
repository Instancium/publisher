namespace Instancium.Publisher.Tui.Components;

using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Renders a unified breadcrumb navigation panel matching the application design system.
/// Isolates header chrome formatting to ensure layout consistency across all TUI views.
/// </summary>
public static class NavigationBreadcrumbsBar
{
    /// <summary>
    /// Renders a rounded panel displaying the current hierarchical navigation trajectory.
    /// </summary>
    /// <param name="segments">Ordered hierarchical path labels (e.g., ["Projects", "RepoName", "Action"]).</param>
    public static void Render(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Length == 0)
        {
            return;
        }

        var navGrid = new Grid()
            .AddColumn(new GridColumn())
            .AddColumn(new GridColumn().RightAligned());

        var formattedSegments = new List<string>();
        for (int i = 0; i < segments.Length; i++)
        {
            string escaped = Markup.Escape(segments[i]);
            if (i == segments.Length - 1)
            {
                formattedSegments.Add($"[bold cyan]{escaped}[/]");
            }
            else if (i == segments.Length - 2)
            {
                formattedSegments.Add($"[bold white]{escaped}[/]");
            }
            else
            {
                formattedSegments.Add($"[grey]{escaped}[/]");
            }
        }

        string breadcrumbTrail = string.Join(" [dim]>[/] ", formattedSegments);

        navGrid.AddRow(
            $"[dim]Navigation:[/] {breadcrumbTrail}",
            "[dim]👇[/]");

        AnsiConsole.Write(
            new Panel(navGrid)
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Grey)
                .Padding(2, 0)
                .Expand());
    }
}