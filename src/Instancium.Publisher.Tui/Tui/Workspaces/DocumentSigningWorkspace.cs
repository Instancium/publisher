namespace Instancium.Publisher.Tui.Workspaces;

using Instancium.Publisher.Application.State;
using Instancium.Publisher.Application.Workflows;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Tui.Abstractions;
using Instancium.Publisher.Tui.Components;
using Spectre.Console;

/// <summary>
/// Interactive 3-step TUI workspace for signing standalone immutable documents (e.g., Instancium_Doctrine.pdf)
/// into a verifiable manifest + detached signature pair, with optional Git publication to instancium-hub.
/// </summary>
public sealed class DocumentSigningWorkspace : ITuiWorkspace
{
    private readonly DocumentSigningWorkflow _workflow;
    private readonly ProjectContext _projectContext;

    public string Name => "Document Signing Center";
    public string Description => "Sign standalone documents (Doctrine PDF) with Release Key";

    public DocumentSigningWorkspace(
        DocumentSigningWorkflow workflow,
        ProjectContext projectContext)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        try
        {
            NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Document Signing", "1/3 Select File");

            string sourcePath = PromptExistingFilePath();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return;
            }

            string defaultOutputDir = Path.GetDirectoryName(sourcePath) ?? _projectContext.WorkingDirectory;
            string outputDirectory = AnsiConsole.Prompt(
                new TextPrompt<string>("Output directory for signed bundle:")
                    .DefaultValue(defaultOutputDir));

            AnsiConsole.WriteLine();
            NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Document Signing", "2/3 Metadata");

            string productName = AnsiConsole.Prompt(
                new TextPrompt<string>("Product identifier:")
                    .DefaultValue("Instancium.Doctrine"));

            SemanticVersion? version = PromptSemanticVersion();
            if (version is null)
            {
                return;
            }

            string bundlePrefix = AnsiConsole.Prompt(
                new TextPrompt<string>("Manifest/signature file prefix:")
                    .DefaultValue("doctrine"));

            string manifestFileName = $"{bundlePrefix.Trim()}.manifest.json";
            string signatureFileName = $"{bundlePrefix.Trim()}.sig";

            AnsiConsole.WriteLine();
            NavigationBreadcrumbsBar.Render("Projects", _projectContext.Name, "Document Signing", "3/3 Sign & Publish");

            AnsiConsole.MarkupLine("[dim]Hashing document and signing manifest...[/]");
            DocumentSigningResult result = await _workflow.SignDocumentAsync(
                new DocumentSigningRequest(
                    sourcePath,
                    outputDirectory,
                    productName,
                    version,
                    manifestFileName,
                    signatureFileName),
                ct);

            RenderSignedSummary(result, productName, version);

            string publishChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("\nCommit and push the signed bundle to a Git repository?")
                    .HighlightStyle(new Style(Color.Cyan, decoration: Decoration.Bold))
                    .AddChoices("Skip (Keep files locally)", "Commit & Push to Git"));

            if (publishChoice == "Commit & Push to Git")
            {
                string repoDir = AnsiConsole.Prompt(
                    new TextPrompt<string>("Target Git repository directory:")
                        .DefaultValue(_projectContext.WorkingDirectory));

                string fileName = Path.GetFileName(result.TargetFilePath);
                string defaultCommitMessage = $"chore(doctrine): sign {fileName} v{version}";
                string commitMessage = AnsiConsole.Prompt(
                    new TextPrompt<string>("Commit message:")
                        .DefaultValue(defaultCommitMessage));

                string pushedBranch = string.Empty;
                await TuiProgressReporter.RunWithLiveStatusAsync(
                    "Committing and pushing signed bundle...",
                    async () => { pushedBranch = await _workflow.PublishToGitAsync(repoDir, commitMessage, ct); },
                    ct);

                AnsiConsole.MarkupLine($"[bold green]Signed bundle committed and pushed to '{Markup.Escape(pushedBranch)}'.[/]");
            }

            PauseAndReturn();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[bold red]Document Signing Failed:[/] {Markup.Escape(ex.Message)}");
            PauseAndReturn();
        }
    }

    private static string PromptExistingFilePath()
    {
        while (true)
        {
            string input = AnsiConsole.Prompt(
                new TextPrompt<string>("Path to document file (empty to cancel):")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string cleaned = input.Trim().Trim('"');
            if (File.Exists(cleaned))
            {
                return Path.GetFullPath(cleaned);
            }

            AnsiConsole.MarkupLine($"[red]File not found:[/] {Markup.Escape(cleaned)}");
        }
    }

    private static SemanticVersion? PromptSemanticVersion()
    {
        while (true)
        {
            string raw = AnsiConsole.Prompt(
                new TextPrompt<string>("Document version (SemVer 2.0, empty to cancel):")
                    .DefaultValue("1.0.0")
                    .AllowEmpty());

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return SemanticVersion.Parse(raw);
            }
            catch (FormatException)
            {
                AnsiConsole.MarkupLine($"[red]'{Markup.Escape(raw)}' is not a valid SemVer 2.0 string. Try again.[/]");
            }
        }
    }

    private static void RenderSignedSummary(
        DocumentSigningResult result,
        string productName,
        SemanticVersion version)
    {
        var panel = new Panel(
            $"Product:   [bold white]{Markup.Escape(productName)}[/] [dim]v{version}[/]\n" +
            $"Document:  [silver]{Markup.Escape(result.TargetFilePath)}[/] [dim]({result.Size} bytes)[/]\n" +
            $"SHA-256:   [cyan]{result.Sha256}[/]\n" +
            $"Manifest:  [green]{Markup.Escape(result.ManifestPath)}[/]\n" +
            $"Signature: [green]{Markup.Escape(result.SignaturePath)}[/]")
            .Header("[bold green]Document Signed Successfully[/]")
            .BorderColor(Color.Green)
            .RoundedBorder()
            .Expand();

        AnsiConsole.Write(panel);
    }

    private static void PauseAndReturn()
    {
        AnsiConsole.MarkupLine("\n[dim]Press ANY KEY to return to main menu...[/]");
        Console.ReadKey(intercept: true);
    }
}
