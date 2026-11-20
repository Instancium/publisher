using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;
using Instancium.Publisher.Application.State;
using System.Text;

namespace Instancium.Publisher.Application.Pipeline.Steps;

/// <summary>
/// Merges the prepared release branch into the target branch, tags and publishes the release,
/// and backports the tagged target branch commit into the source branch to keep branch topology aligned.
/// Prefers fast-forward merges to avoid redundant merge commits, falling back to a forced merge
/// only when branches have diverged.
/// </summary>
public class MergeAndPublishStep
{
    private readonly IGitService _gitService;
    private readonly IProcessExecutionService _processService;
    private readonly IProgressReporter _progressReporter;
    private readonly IReleasePublisher _releasePublisher;
    private readonly ProjectContext _projectContext;

    public MergeAndPublishStep(
        IGitService gitService,
        IProcessExecutionService processService,
        IProgressReporter progressReporter,
        IReleasePublisher releasePublisher,
        ProjectContext projectContext)
    {
        _gitService = gitService;
        _processService = processService;
        _progressReporter = progressReporter;
        _releasePublisher = releasePublisher;
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

        await _gitService.CheckoutBranchAsync(targetBranch, false, ct);

        var pullOutputBuilder = new StringBuilder();
        int pullExitCode = await _processService.ExecuteAsync(
            "git", ["pull", "origin", targetBranch], _projectContext.WorkingDirectory,
            msg => pullOutputBuilder.AppendLine(msg),
            msg => pullOutputBuilder.AppendLine(msg),
            ct);

        if (pullExitCode != 0)
        {
            await _processService.ExecuteAsync("git", ["merge", "--abort"], _projectContext.WorkingDirectory, null, null, ct);
            throw new InvalidOperationException($"Failed to pull latest changes for {targetBranch}. Exit code: {pullExitCode}.\nGit Output: {pullOutputBuilder}");
        }

        await MergeBranchFastForwardOrForceAsync(
            context.ReleaseBranch,
            fallbackCommitMessage: $"chore: merge release {versionTag}",
            operationName: "release merge",
            ct);

        await _gitService.TagAndPushAsync(versionTag, ct);
        await _gitService.PushBranchAsync(targetBranch, force: false, ct);

        if (!string.Equals(context.SourceBranch, targetBranch, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(context.SourceBranch))
        {
            await _progressReporter.ReportAsync($"Backporting release {versionTag} from {targetBranch} to {context.SourceBranch}...", ct);
            await _gitService.CheckoutBranchAsync(context.SourceBranch, false, ct);

            // Merge targetBranch (not ReleaseBranch) so SourceBranch receives the exact tagged commit
            // and remains topologically aligned for subsequent `git tag --merged HEAD` lookups.
            await MergeBranchFastForwardOrForceAsync(
                targetBranch,
                fallbackCommitMessage: $"chore: backport release {versionTag} to {context.SourceBranch}",
                operationName: "backport merge",
                ct);

            await _gitService.PushBranchAsync(context.SourceBranch, force: false, ct);
        }

        await _releasePublisher.PublishAsync(
            new ReleasePublicationRequest(
                versionTag,
                $"Release {versionTag}",
                context.ReleaseNotes,
                context.NewVersion.IsPreRelease),
            ct);
    }

    private async Task MergeBranchFastForwardOrForceAsync(
        string branchToMerge,
        string fallbackCommitMessage,
        string operationName,
        CancellationToken ct)
    {
        int ffExitCode = await _processService.ExecuteAsync(
            "git", ["merge", "--ff-only", branchToMerge], _projectContext.WorkingDirectory,
            null, null, ct);

        if (ffExitCode == 0)
        {
            return;
        }

        var mergeOutputBuilder = new StringBuilder();
        await _processService.ExecuteAsync(
            "git", ["merge", "--no-commit", "--no-ff", branchToMerge], _projectContext.WorkingDirectory,
            msg => mergeOutputBuilder.AppendLine(msg),
            msg => mergeOutputBuilder.AppendLine(msg),
            ct);

        await _processService.ExecuteAsync(
            "git", ["checkout", branchToMerge, "--", "."], _projectContext.WorkingDirectory,
            null, null, ct);

        await _processService.ExecuteAsync(
            "git", ["add", "-A"], _projectContext.WorkingDirectory,
            null, null, ct);

        int commitCode = await _processService.ExecuteAsync(
            "git", ["commit", "-m", fallbackCommitMessage], _projectContext.WorkingDirectory,
            msg => mergeOutputBuilder.AppendLine(msg),
            msg => mergeOutputBuilder.AppendLine(msg),
            ct);

        string fullOutput = mergeOutputBuilder.ToString();
        if (commitCode != 0 &&
            !fullOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) &&
            !fullOutput.Contains("Already up to date", StringComparison.OrdinalIgnoreCase))
        {
            await _processService.ExecuteAsync("git", ["merge", "--abort"], _projectContext.WorkingDirectory, null, null, ct);
            throw new InvalidOperationException($"Failed to commit forced {operationName}. Exit code: {commitCode}.\nGit Output:\n{fullOutput}");
        }
    }
}