using Instancium.Publisher.Application.Abstractions;

namespace Instancium.Publisher.Application.Workflows;

public sealed record CommitInspectionResult(
    bool HasChanges,
    bool HasUnpushedCommits,
    string CurrentBranch,
    string Diff,
    string DiffStat);

/// <summary>
/// Orchestrates the commit workflow: staging, empty-state detection, AI commit message streaming,
/// local commit creation, and branch push.
/// </summary>
public sealed class CommitWorkflow
{
    private readonly IGitService _gitService;
    private readonly IAiCommitAssistant _aiAssistant;

    public CommitWorkflow(IGitService gitService, IAiCommitAssistant aiAssistant)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _aiAssistant = aiAssistant ?? throw new ArgumentNullException(nameof(aiAssistant));
    }

    public async Task<CommitInspectionResult> StageAndInspectAsync(CancellationToken ct = default)
    {
        await _gitService.AddAllAsync(ct);

        string diff = await _gitService.GetCachedDiffAsync(ct);
        string diffStat = await _gitService.GetCachedDiffStatAsync(ct);

        if (!string.IsNullOrWhiteSpace(diff))
        {
            return new CommitInspectionResult(true, false, string.Empty, diff, diffStat);
        }

        bool hasUnpushed = await _gitService.HasUnpushedCommitsAsync(ct);
        string branch = hasUnpushed ? await _gitService.GetCurrentBranchAsync(ct) : string.Empty;

        return new CommitInspectionResult(false, hasUnpushed, branch, string.Empty, string.Empty);
    }

    public IAsyncEnumerable<string> StreamCommitMessageAsync(
        string diff,
        string diffStat,
        CancellationToken ct = default)
    {
        return _aiAssistant.StreamCommitMessageAsync(diff, diffStat, ct);
    }

    public async Task<string> CommitAndGetBranchAsync(string commitMessage, CancellationToken ct = default)
    {
        await _gitService.CommitAsync(commitMessage, ct);
        return await _gitService.GetCurrentBranchAsync(ct);
    }

    public Task PushBranchAsync(string branchName, CancellationToken ct = default)
    {
        return _gitService.PushBranchAsync(branchName, force: false, ct);
    }
}
