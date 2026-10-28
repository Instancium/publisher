namespace Instancium.Publisher.Services.Git;

using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Facade over git CLI operations executed in the active project directory.
/// </summary>
public sealed class GitService : IGitService
{
    private readonly IProcessExecutionService _processRunner;
    private readonly ProjectContext _projectContext;

    public GitService(IProcessExecutionService processRunner, ProjectContext projectContext)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
    }


    /// <summary>
    /// Checks whether the working directory contains uncommitted modifications.
    /// </summary>
    public async Task<bool> HasUncommittedChangesAsync(CancellationToken ct = default)
    {
        string status = await _processRunner.GetOutputAsync("git", "status --porcelain", _projectContext.WorkingDirectory, ct);
        return !string.IsNullOrWhiteSpace(status);
    }

    /// <summary>
    /// Guards destructive pipeline steps against a dirty working directory.
    /// </summary>
    public async Task EnsureCleanWorkingDirectoryAsync(CancellationToken ct = default)
    {
        string status = await _processRunner.GetOutputAsync("git", "status --porcelain", _projectContext.WorkingDirectory, ct);
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException("Working directory is not clean. Commit or stash your changes first.");
        }
    }

    /// <summary>
    /// Stages all modifications in the working directory.
    /// </summary>
    public Task AddAllAsync(CancellationToken ct = default) => ExecuteNoOutputAsync("add -A", ct);

    /// <summary>
    /// Returns the staged diff payload for AI analysis.
    /// </summary>
    public Task<string> GetCachedDiffAsync(CancellationToken ct = default) => _processRunner.GetOutputAsync("git", "diff --cached -M", _projectContext.WorkingDirectory, ct);

    /// <summary>
    /// Returns the staged diff volume statistics.
    /// </summary>
    public Task<string> GetCachedDiffStatAsync(CancellationToken ct = default) => _processRunner.GetOutputAsync("git", "diff --cached -M --stat", _projectContext.WorkingDirectory, ct);

    /// <summary>
    /// Creates a local commit using a temporary message file to avoid shell escaping issues.
    /// </summary>
    public async Task CommitAsync(string message, CancellationToken ct = default)
    {
        string tempFilePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFilePath, message, ct);
            await ExecuteNoOutputAsync($"commit -F \"{tempFilePath}\"", ct);
        }
        finally
        {
            if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
        }
    }

    /// <summary>
    /// Returns the name of the currently checked out branch.
    /// </summary>
    public Task<string> GetCurrentBranchAsync(CancellationToken ct = default)
        => _processRunner.GetOutputAsync("git", "branch --show-current", _projectContext.WorkingDirectory, ct);

    /// <summary>
    /// Returns the absolute path of the git repository root directory.
    /// </summary>
    public Task<string> GetRepositoryRootAsync(CancellationToken ct = default)
        => _processRunner.GetOutputAsync("git", "rev-parse --show-toplevel", _projectContext.WorkingDirectory, ct);

    /// <summary>
    /// Switches branches, optionally creating a new one.
    /// </summary>
    public Task CheckoutBranchAsync(string branchName, bool createNew = false, CancellationToken ct = default)
    {
        string args = createNew ? $"-b {branchName}" : branchName;
        return ExecuteNoOutputAsync($"checkout {args}", ct);
    }

    /// <summary>
    /// Pushes a branch to the remote, optionally forcing the update.
    /// </summary>
    public Task PushBranchAsync(string branchName, bool force = false, CancellationToken ct = default)
    {
        string args = force ? $"push -u origin {branchName} --force" : $"push origin {branchName}";
        return ExecuteNoOutputAsync(args, ct);
    }

    /// <summary>
    /// Fetches all remotes without merging.
    /// </summary>
    public Task FetchAllAsync(CancellationToken ct = default)
        => ExecuteNoOutputAsync("fetch --all", ct);

    /// <summary>
    /// Deletes a local branch, tolerating already deleted branches.
    /// </summary>
    public async Task DeleteLocalBranchAsync(string branchName, CancellationToken ct = default)
    {
        try
        {
            await ExecuteNoOutputAsync($"branch -D {branchName}", ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    public async Task DeleteRemoteBranchAsync(string branchName, CancellationToken ct = default)
    {
        var errorBuilder = new StringBuilder();
        int exitCode = await _processRunner.ExecuteAsync(
            "git",
            $"push origin --delete {branchName}",
            _projectContext.WorkingDirectory,
            null,
            msg => errorBuilder.AppendLine(msg),
            ct);
        if (exitCode != 0)
        {
            string error = errorBuilder.ToString();
            if (!error.Contains("remote ref does not exist", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("couldn't find remote ref", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("unable to delete", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Failed to delete remote branch '{branchName}'. Exit code: {exitCode}. Error: {error}");
            }
        }
    }

    /// <summary>
    /// Returns the most recent tag by creation date across ALL branches.
    /// Reachability-independent by design: release tags are created on main merge commits
    /// and are never ancestors of development branches, so 'git describe' cannot resolve them there.
    /// Returns an empty string when the repository has no tags yet.
    /// </summary>
    public async Task<string> GetLatestTagAsync(CancellationToken ct = default)
    {
        string output = await _processRunner.GetOutputAsync("git", "tag --sort=-creatordate", _projectContext.WorkingDirectory, ct);
        string? latestTag = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return latestTag ?? string.Empty;
    }

    /// <summary>
    /// Returns the commit log for changelog generation.
    /// When a starting tag is provided the range is bounded by it; merge commits are excluded
    /// to keep release-engineering noise out of the AI analysis window.
    /// </summary>
    public Task<string> GetCommitLogAsync(string? fromTag, CancellationToken ct = default)
    {
        string range = string.IsNullOrWhiteSpace(fromTag) ? "-n 20" : $"-n 50 {fromTag.Trim()}..HEAD";
        return _processRunner.GetOutputAsync("git", $"log {range} --oneline --no-merges --name-status -M", _projectContext.WorkingDirectory, ct);
    }

    /// <summary>
    /// Creates a tag on HEAD and pushes it to the remote.
    /// </summary>
    public async Task TagAndPushAsync(string tagName, CancellationToken ct = default)
    {
        await ExecuteNoOutputAsync($"tag {tagName}", ct);
        await ExecuteNoOutputAsync($"push origin {tagName}", ct);
    }

    /// <summary>
    /// Discards all local modifications and untracked files (destructive rollback).
    /// </summary>
    public async Task RestoreAllAsync(CancellationToken ct = default)
    {
        await ExecuteNoOutputAsync("reset --hard HEAD", ct);
        await ExecuteNoOutputAsync("clean -fd", ct);
    }

    private async Task ExecuteNoOutputAsync(string gitArguments, CancellationToken ct)
    {
        var errorBuilder = new StringBuilder();
        int exitCode = await _processRunner.ExecuteAsync("git", gitArguments, _projectContext.WorkingDirectory, null, msg => errorBuilder.AppendLine(msg), ct);
        if (exitCode != 0)
        {
            string errorDetails = errorBuilder.ToString().Trim();
            throw new InvalidOperationException($"Git command 'git {gitArguments}' failed with exit code {exitCode}. Error Details: {errorDetails}");
        }
    }
}