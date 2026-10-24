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
            // ФАЗА 1: Файловые операции (здесь спиннер безопасен, так как логов нет)
            await AnsiConsole.Status().StartAsync("Staging files and analyzing diff...", async ctx =>
            {
                await _addStep.ExecuteAsync(ct);
            });

            string diff = await _gitService.GetCachedDiffAsync(ct);
            string diffStat = await _gitService.GetCachedDiffStatAsync(ct);

            if (string.IsNullOrWhiteSpace(diff))
            {
                AnsiConsole.MarkupLine("[yellow]No changes staged for commit. Working directory is clean.[/]");
                PauseAndReturn();
                return;
            }

            // ФАЗА 2: Подключение и Map-Reduce
            // Используем статичный текст вместо спиннера, чтобы терминал не сломал курсор при скроллинге.
            AnsiConsole.MarkupLine("[dim italic]Transmitting payload to AI model...[/]");

            await using var enumerator = _aiAssistant.StreamCommitMessageAsync(diff, diffStat, ct).GetAsyncEnumerator(ct);

            // Запрашиваем первый токен. В это время Assistant выведет логи "Analyzing part X/Y..." (если они есть)
            bool hasData = await enumerator.MoveNextAsync();

            // ФАЗА 3: Отрисовка чистовика
            // Этот блок нарисуется СТРОГО ПОСЛЕ всех логов Map-Reduce и до начала потока токенов.
            AnsiConsole.MarkupLine("\n[green]Generating Commit Message:[/]");
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());

            var messageBuilder = new StringBuilder();

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

            // ФАЗА 4: Подтверждение и Push
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nProceed with this commit?")
                    .AddChoices("Approve and Push", "Abort (Keep staged)"));

            if (choice == "Approve and Push")
            {
                await AnsiConsole.Status().StartAsync("Committing and pushing to remote...", async ctx =>
                {
                    await _pushStep.ExecuteAsync(context, ct);
                });
                AnsiConsole.MarkupLine("[bold green]Successfully committed and pushed changes![/]");
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

    private static void PauseAndReturn()
    {
        AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
        Console.ReadKey(intercept: true);
    }
}