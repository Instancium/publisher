using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;

namespace Instancium.Publisher.Pipeline.Steps;

public class GitCommitAndPushStep
{
    private readonly IGitService _gitService;

    public GitCommitAndPushStep(IGitService gitService) => _gitService = gitService;

    public async Task ExecuteAsync(PipelineContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.CommitMessage))
        {
            throw new InvalidOperationException("Commit message is empty. Nothing to commit.");
        }

        await _gitService.CommitAsync(context.CommitMessage, ct);

        string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
        await _gitService.PushBranchAsync(currentBranch, force: false, ct);
    }
}