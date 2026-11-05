namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Core.Interfaces;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

public sealed class TuiProgressReporter : IProgressReporter
{
    public static readonly AsyncLocal<StatusContext?> AmbientStatus = new();

    public ValueTask ReportAsync(string message, CancellationToken ct = default)
    {
        if (AmbientStatus.Value is { } statusCtx)
        {
            statusCtx.Status($"[cyan]{Markup.Escape(message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReportWarningAsync(string message, CancellationToken ct = default)
    {
        if (AmbientStatus.Value is { } statusCtx)
        {
            statusCtx.Status($"[yellow]{Markup.Escape(message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
        }

        return ValueTask.CompletedTask;
    }
}