namespace Instancium.Publisher.Core.Interfaces;

using System.Threading;
using System.Threading.Tasks;

public interface IGitService
{
    Task EnsureCleanWorkingDirectoryAsync(CancellationToken ct = default);
    Task AddAllAsync(CancellationToken ct = default);
    Task<string> GetCachedDiffAsync(CancellationToken ct = default);
    Task<string> GetCachedDiffStatAsync(CancellationToken ct = default);
    Task CommitAsync(string message, CancellationToken ct = default);
    Task<string> GetCurrentBranchAsync(CancellationToken ct = default);
    Task<string> GetRepositoryRootAsync(CancellationToken ct = default);
    Task CheckoutBranchAsync(string branchName, bool createNew = false, CancellationToken ct = default);
    Task PushBranchAsync(string branchName, bool force = false, CancellationToken ct = default);
    Task FetchAllAsync(CancellationToken ct = default);
    Task DeleteLocalBranchAsync(string branchName, CancellationToken ct = default);
    Task DeleteRemoteBranchAsync(string branchName, CancellationToken ct = default);
    Task<string> GetLatestTagAsync(CancellationToken ct = default);
    Task<string> GetCommitLogAsync(string? fromTag, CancellationToken ct = default);
    Task TagAndPushAsync(string tagName, CancellationToken ct = default);
    Task RestoreAllAsync(CancellationToken ct = default);

    Task<bool> HasUncommittedChangesAsync(CancellationToken ct = default);
}