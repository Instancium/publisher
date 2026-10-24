using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using System.Text;

namespace Instancium.Publisher.Pipeline.Steps;

public class MergeAndPublishStep
{
    private readonly IGitService _gitService;
    private readonly IProcessExecutionService _processService;
    private readonly IProgressReporter _progressReporter;

    public MergeAndPublishStep(IGitService gitService, IProcessExecutionService processService, IProgressReporter progressReporter)
    {
        _gitService = gitService;
        _processService = processService;
        _progressReporter = progressReporter;
    }

    public async Task ExecuteAsync(PipelineContext context, string targetBranch = "main", CancellationToken ct = default)
    {
        if (context.NewVersion == null)
        {
            throw new InvalidOperationException("New version is not set in the pipeline context.");
        }
        if (string.IsNullOrWhiteSpace(context.ReleaseBranch))
        {
            throw new InvalidOperationException("Release branch is not specified in the pipeline context.");
        }
        string versionTag = $"v{context.NewVersion}";
        string mergeMessage = $"chore: merge release {versionTag}";
        await _gitService.CheckoutBranchAsync(targetBranch, false, ct);
        var pullErrorBuilder = new StringBuilder();
        int pullExitCode = await _processService.ExecuteAsync(
            "git", $"pull origin {targetBranch}", string.Empty, null, msg => pullErrorBuilder.AppendLine(msg), ct);
        if (pullExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to pull latest changes for {targetBranch}. Exit code: {pullExitCode}.\nError: {pullErrorBuilder}");
        }
        var mergeErrorBuilder = new StringBuilder();
        int mergeExitCode = await _processService.ExecuteAsync(
            "git", $"merge --no-ff {context.ReleaseBranch} -m \"{mergeMessage}\"", string.Empty, null, msg => mergeErrorBuilder.AppendLine(msg), ct);
        if (mergeExitCode != 0)
        {
            throw new InvalidOperationException($"Merge failed with conflicts or errors. Exit code: {mergeExitCode}.\nError: {mergeErrorBuilder}");
        }
        await _gitService.TagAndPushAsync(versionTag, ct);
        await _gitService.PushBranchAsync(targetBranch, force: false, ct);
        if (!string.Equals(context.SourceBranch, targetBranch, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(context.SourceBranch))
        {
            await _progressReporter.ReportAsync($"Backporting release {versionTag} to {context.SourceBranch}...", ct);
            await _gitService.CheckoutBranchAsync(context.SourceBranch, false, ct);
            var backportErrorBuilder = new StringBuilder();
            int backportExitCode = await _processService.ExecuteAsync(
                "git",
                $"merge --no-ff {context.ReleaseBranch} -m \"chore: backport release {versionTag} to {context.SourceBranch}\"",
                string.Empty,
                null,
                msg => backportErrorBuilder.AppendLine(msg),
                ct);
            if (backportExitCode != 0)
            {
                throw new InvalidOperationException($"Backport merge failed. Exit code: {backportExitCode}.\nError: {backportErrorBuilder}");
            }
            await _gitService.PushBranchAsync(context.SourceBranch, force: false, ct);
        }
        await CreateGitHubReleaseAsync(versionTag, context.ReleaseNotes, context.NewVersion.IsPreRelease, ct);
    }

    private async Task CreateGitHubReleaseAsync(string tag, string releaseNotes, bool isPreRelease, CancellationToken ct)
    {
        string notes = string.IsNullOrWhiteSpace(releaseNotes) ? "Automated release via Instancium.Publisher" : releaseNotes;
        string tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFilePath, notes, ct);

            // 2. Append the --prerelease flag if the version requires it
            string preReleaseFlag = isPreRelease ? " --prerelease" : string.Empty;
            string arguments = $"release create {tag} -t \"Release {tag}\" -F \"{tempFilePath}\"{preReleaseFlag}";

            var ghErrorBuilder = new StringBuilder();
            int ghExitCode = await _processService.ExecuteAsync(
                "gh",
                arguments,
                string.Empty,
                onStdErr: msg => ghErrorBuilder.AppendLine(msg),
                ct: ct);

            if (ghExitCode != 0)
            {
                throw new InvalidOperationException($"GitHub CLI (gh) release creation failed. Exit code: {ghExitCode}.\nError: {ghErrorBuilder}");
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
}