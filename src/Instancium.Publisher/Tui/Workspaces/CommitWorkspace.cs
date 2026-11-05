namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.Pipeline.Steps;
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
    private readonly GitAddAllStep _addStep;
    private readonly GitCommitAndPushStep _pushStep;

    public string Name => "Commit Control Center";
    public string Description => "Stage files and generate AI commit message (Streaming)";

    public CommitWorkspace(
        IGitService gitService,
        IAiCommitAssistant aiAssistant,
        GitAddAllStep addStep,
        GitCommitAndPushStep pushStep)
    {
        _gitService = gitService;
        _aiAssistant = aiAssistant;
        _addStep = addStep;
        _pushStep = pushStep;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        AnsiConsole.MarkupLine("[cyan]Initializing Auto-Commit Pipeline...[/]");
        var context = new PipelineContext();

        try
        {
            await AnsiConsole.Status().StartAsync("Staging files and analyzing diff...", async ctx =>
            {
                await _addStep.ExecuteAsync(ct);
            });

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

            // Phase 1: Floating status with a spinner (updates in-place during Map-Reduce)
            await AnsiConsole.Status()
           .Spinner(Spinner.Known.Dots)
           .StartAsync("[cyan]Transmitting payload to AI model...[/]", async ctx =>
           {
               TuiProgressReporter.AmbientStatus.Value = ctx;
               try
               {
                   hasData = await enumerator.MoveNextAsync();
               }
               finally
               {
                   TuiProgressReporter.AmbientStatus.Value = null;
               }
           });

            // Phase 2: Status is closed and cleared. Clean token streaming is underway, with no interference from the spinner.
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
                await AnsiConsole.Status().StartAsync("Committing changes locally...", async ctx =>
                {
                    await _gitService.CommitAsync(context.CommitMessage, ct);
                });

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
                await AnsiConsole.Status().StartAsync($"Pushing '{Markup.Escape(branchName)}' to remote...", async ctx =>
                {
                    await _gitService.PushBranchAsync(branchName, force: false, ct);
                });

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