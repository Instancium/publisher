using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.State;
using System.Text;

namespace Instancium.Publisher.Infrastructure.GitHub;

/// <summary>
/// Publishes releases via GitHub CLI (<c>gh release create</c>), falling back to exporting
/// release notes to a local temporary file if <c>gh</c> fails or is unavailable.
/// </summary>
public sealed class GitHubCliReleasePublisher : IReleasePublisher
{
    private readonly IProcessExecutionService _processService;
    private readonly IProgressReporter _progressReporter;
    private readonly ProjectContext _projectContext;

    public GitHubCliReleasePublisher(
        IProcessExecutionService processService,
        IProgressReporter progressReporter,
        ProjectContext projectContext)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
    }

    public async Task PublishAsync(ReleasePublicationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string notes = string.IsNullOrWhiteSpace(request.Notes)
            ? $"Automated release {request.TagName} via Instancium.Publisher"
            : request.Notes;
        string tempFilePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFilePath, notes, ct);
            string preReleaseFlag = request.IsPreRelease ? " --prerelease" : string.Empty;
            string arguments = $"release create {request.TagName} -t \"{request.Title}\" -F \"{tempFilePath}\"{preReleaseFlag}";

            var ghOutputBuilder = new StringBuilder();
            int ghExitCode = await _processService.ExecuteAsync(
                "gh",
                arguments,
                _projectContext.WorkingDirectory,
                onStdOut: msg => ghOutputBuilder.AppendLine(msg),
                onStdErr: msg => ghOutputBuilder.AppendLine(msg),
                ct: ct);

            if (ghExitCode != 0)
            {
                await HandleGitHubCliFailureAsync(request.TagName, notes, ghOutputBuilder.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            await HandleGitHubCliFailureAsync(request.TagName, notes, ex.Message, ct);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private async Task HandleGitHubCliFailureAsync(string tag, string releaseNotes, string errorReason, CancellationToken ct)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "Instancium.Publisher");
        Directory.CreateDirectory(tempDirectory);

        string fallbackPath = Path.Combine(tempDirectory, $"RELEASE_NOTES_{tag}.md");
        await File.WriteAllTextAsync(fallbackPath, releaseNotes, ct);

        await _progressReporter.ReportWarningAsync(
            $"GitHub CLI ('gh') is unavailable or unauthenticated (Exit details: {errorReason.Trim()}).", ct);
        await _progressReporter.ReportWarningAsync(
            $"Release notes exported to '{fallbackPath}'. Tag {tag} was pushed successfully.", ct);
    }
}
