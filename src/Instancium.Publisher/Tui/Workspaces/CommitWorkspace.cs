namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using Spectre.Console;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public sealed class CommitWorkspace : ITuiWorkspace
{
    private readonly IGitService _gitService;
    private readonly IAiCommitAssistant _aiAssistant;

    public string Name => "Commit Control Center";
    public string Description => "Stage files and generate AI commit message (Streaming)";

    public CommitWorkspace(
        IGitService gitService,
        IAiCommitAssistant aiAssistant)
    {
        _gitService = gitService;
        _aiAssistant = aiAssistant;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var context = new PipelineContext();

        try
        {
            await TuiProgressReporter.RunWithLiveStatusAsync(
                "Staging files and analyzing diff...",
                async () => await _gitService.AddAllAsync(ct), ct);

            string diff = await _gitService.GetCachedDiffAsync(ct);
            string diffStat = await _gitService.GetCachedDiffStatAsync(ct);

            if (string.IsNullOrWhiteSpace(diff))
            {
                if (await _gitService.HasUnpushedCommitsAsync(ct))
                {
                    string branch = await _gitService.GetCurrentBranchAsync(ct);
                    AnsiConsole.MarkupLine($"[yellow]Working directory is clean, but branch '{Markup.Escape(branch)}' has unpushed local commit(s).[/]");

                    var pushPendingChoice = AnsiConsole.Prompt(
                        new SelectionPrompt<string>()
                            .Title("\nWould you like to push pending local commits to remote?")
                            .AddChoices("Push to Remote", "Return to Menu"));

                    if (pushPendingChoice == "Push to Remote")
                    {
                        await PushWithRetryLoopAsync(branch, ct);
                    }

                    PauseAndReturn();
                    return;
                }

                AnsiConsole.MarkupLine("[yellow]No changes staged for commit. Working directory is clean.[/]");
                PauseAndReturn();
                return;
            }

            var messageBuilder = new StringBuilder();

            await using var enumerator = _aiAssistant.StreamCommitMessageAsync(diff, diffStat, ct).GetAsyncEnumerator(ct);
            bool hasData = false;

            // Phase 1: Progress bar + spinner status (automatically clears before streaming)
            await TuiProgressReporter.RunWithLiveStatusAsync(
                "Transmitting payload to AI model...",
                async () => { hasData = await enumerator.MoveNextAsync(); },
                ct);

            // Phase 2: Clean token streaming
            AnsiConsole.MarkupLine("\n[green]Generating Commit Message:[/]");
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());

            if (hasData)
            {
                CliMarkup.Print($"[silver]{Markup.Escape(enumerator.Current)}[/]");
                messageBuilder.Append(enumerator.Current);

                while (await enumerator.MoveNextAsync())
                {
                    CliMarkup.Print($"[silver]{Markup.Escape(enumerator.Current)}[/]");
                    messageBuilder.Append(enumerator.Current);
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
            context.CommitMessage = messageBuilder.ToString().Trim();

            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nProceed with this commit?")
                    .AddChoices("Approve and Push", "Abort (Keep staged)"));

            if (choice == "Approve and Push")
            {
                await TuiProgressReporter.RunWithLiveStatusAsync(
                    "Committing changes locally...",
                    async () => await _gitService.CommitAsync(context.CommitMessage, ct),
                    ct);

                AnsiConsole.MarkupLine("[green]Local commit created.[/]");
                string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
                await PushWithRetryLoopAsync(currentBranch, ct);
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Commit aborted by user. Files remain staged.[/]");
            }

            PauseAndReturn();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Commit Pipeline Failed:[/] {Markup.Escape(ex.Message)}");
            PauseAndReturn();
        }
    }

    private async Task PushWithRetryLoopAsync(string branchName, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await TuiProgressReporter.RunWithLiveStatusAsync(
                    $"Pushing '{branchName}' to remote...",
                    async () => await _gitService.PushBranchAsync(branchName, force: false, ct),
                    ct);

                AnsiConsole.MarkupLine("[bold green]Successfully pushed changes to remote![/]");
                return;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"\n[bold red]Push Failed:[/] {Markup.Escape(ex.Message)}");

                var retryChoice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("\nLocal commit is saved. Retry pushing to remote?")
                        .AddChoices("Retry Push", "Leave Committed Locally (Exit)"));

                if (retryChoice != "Retry Push")
                {
                    AnsiConsole.MarkupLine("[yellow]Changes remain committed locally. You can push them later.[/]");
                    return;
                }
            }
        }
    }

    private static void PauseAndReturn()
    {
        AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
        Console.ReadKey(intercept: true);
    }
}