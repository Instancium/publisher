namespace Instancium.Publisher.Tui;

using Instancium.Publisher.Core.Interfaces;
using Spectre.Console;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class TuiProgressReporter : IProgressReporter
{
    public static readonly AsyncLocal<LiveStatusController?> AmbientLive = new();

    [GeneratedRegex(@"\b(?<current>\d+)\s*/\s*(?<total>\d+)\b", RegexOptions.Compiled)]
    private static partial Regex ChunkProgressRegex();

    public ValueTask ReportAsync(string message, CancellationToken ct = default)
    {
        if (AmbientLive.Value is { } live)
        {
            live.UpdateStatus(message, isWarning: false);
        }
        else
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReportWarningAsync(string message, CancellationToken ct = default)
    {
        if (AmbientLive.Value is { } live)
        {
            live.UpdateStatus(message, isWarning: true);
        }
        else
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Starts a two-line live indicator (progress bar on top + spinner and status below).
    /// Automatically clears itself from the screen when the task completes (before streaming begins).
    /// </summary>
    public static async Task RunWithLiveStatusAsync(
        string initialStatus,
        Func<Task> action,
        CancellationToken ct = default)
    {
        // Graceful degradation for CI/CD and non-interactive terminals (pipe / redirect)
        if (!AnsiConsole.Profile.Capabilities.Ansi || !AnsiConsole.Profile.Capabilities.Interactive)
        {
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(initialStatus)}[/]");
            await action();
            return;
        }

        var controller = new LiveStatusController(initialStatus);
        AmbientLive.Value = controller;

        try
        {
            await AnsiConsole.Live(controller.BuildRenderable())
                .AutoClear(true)
                .Overflow(VerticalOverflow.Crop)
                .StartAsync(async ctx =>
                {
                    using var timerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var renderLoop = Task.Run(async () =>
                    {
                        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(80));
                        try
                        {
                            while (await timer.WaitForNextTickAsync(timerCts.Token))
                            {
                                controller.AdvanceSpinner();
                                ctx.UpdateTarget(controller.BuildRenderable());
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }, CancellationToken.None);

                    try
                    {
                        await action();
                    }
                    finally
                    {
                        await timerCts.CancelAsync();
                        try { await renderLoop; } catch { }

                        // Fully clear the live area before text streaming begins
                        ctx.UpdateTarget(new Text(string.Empty));
                    }
                });
        }
        finally
        {
            AmbientLive.Value = null;
        }
    }

    public sealed class LiveStatusController
    {
        private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        private readonly Lock _sync = new();
        private string _statusMessage;
        private bool _isWarning;
        private int _currentChunk;
        private int _totalChunks;
        private int _spinnerIndex;

        public LiveStatusController(string initialStatus)
        {
            _statusMessage = initialStatus;
        }

        public void UpdateStatus(string message, bool isWarning)
        {
            lock (_sync)
            {
                _statusMessage = message;
                _isWarning = isWarning;

                var match = ChunkProgressRegex().Match(message);
                if (match.Success &&
                    int.TryParse(match.Groups["current"].Value, out int current) &&
                    int.TryParse(match.Groups["total"].Value, out int total) &&
                    total > 0)
                {
                    _currentChunk = Math.Clamp(current, 0, total);
                    _totalChunks = total;
                }
            }
        }

        public void AdvanceSpinner()
        {
            lock (_sync)
            {
                _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
            }
        }

        public Rows BuildRenderable()
        {
            lock (_sync)
            {
                int safeWidth = Math.Max(30, AnsiConsole.Profile.Width - 2);
                string spinner = SpinnerFrames[_spinnerIndex];
                string color = _isWarning ? "yellow" : "cyan";

                string trimmedStatus = TrimToWidth(_statusMessage, safeWidth - 4);
                var statusLine = new Markup($"[bold {color}]{spinner}[/] [{color}]{Markup.Escape(trimmedStatus)}[/]");

                if (_totalChunks > 0)
                {
                    const int barWidth = 20;
                    int filled = (int)Math.Round((double)_currentChunk / _totalChunks * barWidth);
                    filled = Math.Clamp(filled, 0, barWidth);

                    string filledBar = new('█', filled);
                    string emptyBar = new('░', barWidth - filled);

                    int done = Math.Max(0, _currentChunk - 1);
                    int running = _currentChunk <= _totalChunks ? 1 : 0;
                    int pending = Math.Max(0, _totalChunks - _currentChunk);

                    var barLine = new Markup(
                        $"[grey][[[/][cyan]{filledBar}[/][dim grey]{emptyBar}[/][grey]]][/] " +
                        $"[bold white]{_currentChunk}/{_totalChunks}[/] [silver]chunks processed[/] " +
                        $"[dim]({running} running, {done} done, {pending} pending)[/]");

                    return new Rows(barLine, statusLine);
                }

                return new Rows(statusLine);
            }
        }

        private static string TrimToWidth(string input, int maxLength)
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
            {
                return input;
            }

            return maxLength > 3 ? string.Concat(input.AsSpan(0, maxLength - 3), "...") : input[..maxLength];
        }
    }
}