namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Core.Interfaces;
using Spectre.Console;
using System.Threading;
using System.Threading.Tasks;

public sealed class TuiProgressReporter : IProgressReporter
{
    public ValueTask ReportAsync(string message, CancellationToken ct = default)
    {
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        return ValueTask.CompletedTask;
    }

    public ValueTask ReportWarningAsync(string message, CancellationToken ct = default)
    {
        AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
        return ValueTask.CompletedTask;
    }
}