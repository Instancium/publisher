using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;

namespace Instancium.Publisher.Application.Workflows;

/// <summary>
/// Executes destructive rollback of an interrupted or aborted release pipeline,
/// restoring the working tree, returning to the source branch, removing release branches,
/// and clearing the pipeline journal.
/// </summary>
public sealed class ReleaseRecoveryService
{
    private readonly IGitService _gitService;
    private readonly IPipelineStateManager _stateManager;

    public ReleaseRecoveryService(IGitService gitService, IPipelineStateManager stateManager)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public async Task ExecuteDestructiveRollbackAsync(PipelineContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        string currentBranch = await _gitService.GetCurrentBranchAsync(ct);

        if (!string.IsNullOrEmpty(context.ReleaseBranch) &&
            string.Equals(currentBranch, context.ReleaseBranch, StringComparison.OrdinalIgnoreCase))
        {
            await _gitService.RestoreAllAsync(ct);

            if (!string.IsNullOrEmpty(context.SourceBranch))
            {
                await _gitService.CheckoutBranchAsync(context.SourceBranch, false, ct);
            }
        }

        if (!string.IsNullOrEmpty(context.ReleaseBranch))
        {
            await _gitService.DeleteLocalBranchAsync(context.ReleaseBranch, ct);
            await _gitService.DeleteRemoteBranchAsync(context.ReleaseBranch, ct);
        }

        await _stateManager.RollbackAsync();
    }
}
