using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Instancium.Publisher.Pipeline.Steps;

public class MergeAndPublishStep
{
    private readonly IGitService _gitService;
    private readonly IProcessExecutionService _processService;
    private readonly IProgressReporter _progressReporter;
    private readonly ProjectContext _projectContext;

    public MergeAndPublishStep(
        IGitService gitService,
        IProcessExecutionService processService,
        IProgressReporter progressReporter,
        ProjectContext projectContext)
    {
        _gitService = gitService;
        _processService = processService;
        _progressReporter = progressReporter;
        _projectContext = projectContext;
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

        // 1. PULL target branch
        var pullOutputBuilder = new StringBuilder();
        int pullExitCode = await _processService.ExecuteAsync(
            "git", $"pull origin {targetBranch}", _projectContext.WorkingDirectory,
            msg => pullOutputBuilder.AppendLine(msg),
            msg => pullOutputBuilder.AppendLine(msg),
            ct);

        if (pullExitCode != 0)
        {
            await _processService.ExecuteAsync("git", "merge --abort", _projectContext.WorkingDirectory, null, null, ct);
            throw new InvalidOperationException($"Failed to pull latest changes for {targetBranch}. Exit code: {pullExitCode}.\nGit Output: {pullOutputBuilder}");
        }

        // 2. MERGE release branch into targetBranch (main) preserving history graph
        var mergeOutputBuilder = new StringBuilder();
        await _processService.ExecuteAsync(
            "git", $"merge --no-commit --no-ff {context.ReleaseBranch}", _projectContext.WorkingDirectory,
            msg => mergeOutputBuilder.AppendLine(msg),
            msg => mergeOutputBuilder.AppendLine(msg),
            ct);

        await _processService.ExecuteAsync(
            "git", $"checkout {context.ReleaseBranch} -- .", _projectContext.WorkingDirectory,
            null, null, ct);

        await _processService.ExecuteAsync(
            "git", "add -A", _projectContext.WorkingDirectory,
            null, null, ct);

        int commitCode = await _processService.ExecuteAsync(
            "git", $"commit -m \"{mergeMessage}\"", _projectContext.WorkingDirectory,
            msg => mergeOutputBuilder.AppendLine(msg),
            msg => mergeOutputBuilder.AppendLine(msg),
            ct);

        string fullMergeOutput = mergeOutputBuilder.ToString();
        if (commitCode != 0 &&
            !fullMergeOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) &&
            !fullMergeOutput.Contains("Already up to date", StringComparison.OrdinalIgnoreCase))
        {
            await _processService.ExecuteAsync("git", "merge --abort", _projectContext.WorkingDirectory, null, null, ct);
            throw new InvalidOperationException($"Failed to commit forced merge. Exit code: {commitCode}.\nGit Output:\n{fullMergeOutput}");
        }

        await _gitService.TagAndPushAsync(versionTag, ct);
        await _gitService.PushBranchAsync(targetBranch, force: false, ct);

        // 3. BACKPORT to SourceBranch (e.g. dev/main)
        if (!string.Equals(context.SourceBranch, targetBranch, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(context.SourceBranch))
        {
            await _progressReporter.ReportAsync($"Backporting release {versionTag} to {context.SourceBranch}...", ct);
            await _gitService.CheckoutBranchAsync(context.SourceBranch, false, ct);

            // Fast-forward SourceBranch to ReleaseBranch first so we don't create a redundant
            // divergent merge commit ("0 files changed") ahead of targetBranch.
            int ffExitCode = await _processService.ExecuteAsync(
                "git", $"merge --ff-only {context.ReleaseBranch}", _projectContext.WorkingDirectory,
                null, null, ct);

            if (ffExitCode != 0)
            {
                var backportOutputBuilder = new StringBuilder();

                await _processService.ExecuteAsync(
                    "git", $"merge --no-commit --no-ff {context.ReleaseBranch}", _projectContext.WorkingDirectory,
                    msg => backportOutputBuilder.AppendLine(msg),
                    msg => backportOutputBuilder.AppendLine(msg),
                    ct);

                await _processService.ExecuteAsync(
                    "git", $"checkout {context.ReleaseBranch} -- .", _projectContext.WorkingDirectory,
                    null, null, ct);

                await _processService.ExecuteAsync(
                    "git", "add -A", _projectContext.WorkingDirectory,
                    null, null, ct);

                int bpCommitCode = await _processService.ExecuteAsync(
                    "git", $"commit -m \"chore: backport release {versionTag} to {context.SourceBranch}\"", _projectContext.WorkingDirectory,
                    msg => backportOutputBuilder.AppendLine(msg),
                    msg => backportOutputBuilder.AppendLine(msg),
                    ct);

                string fullBackportOutput = backportOutputBuilder.ToString();
                if (bpCommitCode != 0 &&
                    !fullBackportOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) &&
                    !fullBackportOutput.Contains("Already up to date", StringComparison.OrdinalIgnoreCase))
                {
                    await _processService.ExecuteAsync("git", "merge --abort", _projectContext.WorkingDirectory, null, null, ct);
                    throw new InvalidOperationException($"Backport merge failed. Exit code: {bpCommitCode}.\nGit Output:\n{fullBackportOutput}");
                }
            }

            await _gitService.PushBranchAsync(context.SourceBranch, force: false, ct);
        }

        await CreateGitHubReleaseAsync(versionTag, context.ReleaseNotes, context.NewVersion.IsPreRelease, ct);
    }

    private async Task CreateGitHubReleaseAsync(string tag, string releaseNotes, bool isPreRelease, CancellationToken ct)
    {
        string notes = string.IsNullOrWhiteSpace(releaseNotes) ? $"Automated release {tag} via Instancium.Publisher" : releaseNotes;
        string tempFilePath = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFilePath, notes, ct);
            string preReleaseFlag = isPreRelease ? " --prerelease" : string.Empty;
            string arguments = $"release create {tag} -t \"Release {tag}\" -F \"{tempFilePath}\"{preReleaseFlag}";

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
                await HandleGitHubCliFailureAsync(tag, notes, ghOutputBuilder.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            await HandleGitHubCliFailureAsync(tag, notes, ex.Message, ct);
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
        string fallbackFileName = $"RELEASE_NOTES_{tag}.md";
        string fallbackPath = Path.Combine(_projectContext.WorkingDirectory, fallbackFileName);

        await File.WriteAllTextAsync(fallbackPath, releaseNotes, ct);

        await _progressReporter.ReportWarningAsync(
            $"GitHub CLI ('gh') is unavailable or unauthenticated (Exit details: {errorReason.Trim()}).", ct);
        await _progressReporter.ReportWarningAsync(
            $"Release notes exported locally to '{fallbackFileName}'. Tag {tag} was pushed successfully.", ct);
    }
}