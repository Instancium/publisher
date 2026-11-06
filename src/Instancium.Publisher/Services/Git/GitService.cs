namespace Instancium.Publisher.Services.Git;

using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides Git repository operations for the release and commit pipelines with transient network retry support.
/// </summary>
public sealed class GitService : IGitService
{
    private const int MaxRemoteRetries = 4;
    private readonly IProcessExecutionService _processRunner;
    private readonly ProjectContext _projectContext;
    private readonly IProgressReporter _progressReporter;
    private readonly ILogger<GitService> _logger;
    private readonly PublisherSettings _settings;

    public GitService(
        IProcessExecutionService processRunner,
        ProjectContext projectContext,
        IProgressReporter progressReporter,
        ILogger<GitService> logger,
        IOptions<PublisherSettings> settings)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<bool> HasUncommittedChangesAsync(CancellationToken ct = default)
    {
        string status = await _processRunner.GetOutputAsync("git", "status --porcelain", _projectContext.WorkingDirectory, ct);
        return !string.IsNullOrWhiteSpace(status);
    }

    public async Task<bool> HasUnpushedCommitsAsync(CancellationToken ct = default)
    {
        try
        {
            string countText = await _processRunner.GetOutputAsync("git", "rev-list @{u}..HEAD --count", _projectContext.WorkingDirectory, ct);
            return int.TryParse(countText.Trim(), out int count) && count > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsureCleanWorkingDirectoryAsync(CancellationToken ct = default)
    {
        string status = await _processRunner.GetOutputAsync("git", "status --porcelain", _projectContext.WorkingDirectory, ct);
        if (!string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException("Working directory is not clean. Commit or stash your changes first.");
        }
    }

    public Task AddAllAsync(CancellationToken ct = default) => ExecuteNoOutputAsync("add -A", ct);

    public Task<string> GetCachedDiffAsync(CancellationToken ct = default)
    {
        var opt = _settings.DiffOptimization;
        string args = "diff --cached -M";

        if (opt.Enabled)
        {
            args += $" -U{opt.ContextLines}";
            if (opt.IgnoreWhitespace)
            {
                args += " -w --ignore-blank-lines";
            }
        }

        return _processRunner.GetOutputAsync("git", args, _projectContext.WorkingDirectory, ct);
    }

    public Task<string> GetCachedDiffStatAsync(CancellationToken ct = default) =>
        _processRunner.GetOutputAsync("git", "diff --cached -M --stat", _projectContext.WorkingDirectory, ct);

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
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public Task<string> GetCurrentBranchAsync(CancellationToken ct = default) =>
        _processRunner.GetOutputAsync("git", "branch --show-current", _projectContext.WorkingDirectory, ct);

    public Task<string> GetRepositoryRootAsync(CancellationToken ct = default) =>
        _processRunner.GetOutputAsync("git", "rev-parse --show-toplevel", _projectContext.WorkingDirectory, ct);

    public Task CheckoutBranchAsync(string branchName, bool createNew = false, CancellationToken ct = default)
    {
        string args = createNew ? $"-b {branchName}" : branchName;
        return ExecuteNoOutputAsync($"checkout {args}", ct);
    }

    public Task PushBranchAsync(string branchName, bool force = false, CancellationToken ct = default)
    {
        string args = force ? $"push -u origin {branchName} --force" : $"push origin {branchName}";
        return ExecuteRemoteWithRetryAsync(args, ct);
    }

    public Task FetchAllAsync(CancellationToken ct = default) =>
        ExecuteRemoteWithRetryAsync("fetch --all", ct);

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
    /// Resolves the highest Semantic Version tag in the repository, falling back to version refname sorting.
    /// </summary>
    public async Task<string> GetLatestTagAsync(bool stableOnly = false, CancellationToken ct = default)
    {
        string output = await _processRunner.GetOutputAsync("git", "tag --sort=-v:refname", _projectContext.WorkingDirectory, ct);
        string[] tags = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tags.Length == 0) return string.Empty;

        string? highestTag = null;
        SemanticVersion? highestVersion = null;

        foreach (string tag in tags)
        {
            if (TryParseSemanticVersion(tag, out SemanticVersion? parsed) && parsed is not null)
            {
                // Ignore pre-releases if we only need a stable anchor
                if (stableOnly && parsed.IsPreRelease)
                {
                    continue;
                }

                if (highestVersion is null || parsed.CompareTo(highestVersion) > 0)
                {
                    highestVersion = parsed;
                    highestTag = tag;
                }
            }
        }

        return highestTag ?? tags[0];
    }

    public Task<string> GetCommitLogAsync(string? fromTag, bool ignorePreReleaseTags = false, CancellationToken ct = default)
    {
        // Git magic: --exclude="*-*" makes --tags ignore any tags containing a hyphen.
        // According to SemVer, pre-releases always contain a hyphen (v1.6.0-rc.1), while stable releases do not (v1.6.0).
        string notTagsArgs = ignorePreReleaseTags
            ? "--not --exclude=\"*-*\" --tags"
            : "--not --tags";

        string range = string.IsNullOrWhiteSpace(fromTag)
            ? "-n 20"
            : $"-n 50 {fromTag.Trim()}..HEAD {notTagsArgs}";

        return _processRunner.GetOutputAsync("git", $"log --oneline --no-merges --name-status -M {range}", _projectContext.WorkingDirectory, ct);
    }


    /// <summary>
    /// Creates or updates an annotated release tag and pushes it to the remote repository with retry support.
    /// </summary>
    public async Task TagAndPushAsync(string tagName, CancellationToken ct = default)
    {
        await ExecuteNoOutputAsync($"tag -a {tagName} -m \"Release {tagName}\" -f", ct);
        await ExecuteRemoteWithRetryAsync($"push origin {tagName} --force", ct);
        // Brief pause after tag push to avoid GitHub ref-lock collisions on the immediate branch push
        await Task.Delay(500, ct);
    }

    public async Task RestoreAllAsync(CancellationToken ct = default)
    {
        await ExecuteNoOutputAsync("reset --hard HEAD", ct);
        await ExecuteNoOutputAsync("clean -fd", ct);
    }

    private static bool TryParseSemanticVersion(string rawTag, out SemanticVersion? version)
    {
        try
        {
            version = SemanticVersion.Parse(rawTag);
            return true;
        }
        catch
        {
            version = null;
            return false;
        }
    }

    private static bool IsTransientRemoteError(string errorText)
    {
        return errorText.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("RPC failed", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("HTTP 502", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("HTTP 503", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("HTTP 504", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("Connection reset", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("unable to access", StringComparison.OrdinalIgnoreCase) ||
               errorText.Contains("The requested URL returned error: 5", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteRemoteWithRetryAsync(string gitArguments, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRemoteRetries; attempt++)
        {
            var errorBuilder = new StringBuilder();
            try
            {
                _logger.LogDebug("Executing: git {Arguments} (Attempt {Attempt}/{Max})", gitArguments, attempt, MaxRemoteRetries);

                int exitCode = await _processRunner.ExecuteAsync(
                    "git",
                    gitArguments,
                    _projectContext.WorkingDirectory,
                    null,
                    msg => errorBuilder.AppendLine(msg),
                    ct);

                if (exitCode == 0) return;

                string errorDetails = errorBuilder.ToString().Trim();
                _logger.LogWarning("Git command failed (Exit code {Code}): {Details}", exitCode, errorDetails);

                if (attempt < MaxRemoteRetries && IsTransientRemoteError(errorDetails))
                {
                    await TriggerRetryDelayAsync(attempt, gitArguments, ct);
                    continue;
                }

                throw new InvalidOperationException($"Git command 'git {gitArguments}' failed with exit code {exitCode}. Error Details: {errorDetails}");
            }
            catch (TimeoutException ex) when (attempt < MaxRemoteRetries)
            {
                _logger.LogWarning(ex, "Git command timed out: {Arguments}", gitArguments);
                await _progressReporter.ReportWarningAsync(
                    $"Git process hung on 'git {gitArguments}'. Terminated by Idle Timeout. Cleaning up locks...", ct);

                string lockPath = Path.Combine(_projectContext.WorkingDirectory, ".git", "index.lock");
                if (File.Exists(lockPath))
                {
                    try { File.Delete(lockPath); } catch { }
                }
                await TriggerRetryDelayAsync(attempt, gitArguments, ct);
            }
        }
    }

    private async Task TriggerRetryDelayAsync(int attempt, string gitArguments, CancellationToken ct)
    {
        int delaySeconds = (int)Math.Pow(2, attempt);
        await _progressReporter.ReportWarningAsync(
            $"Retrying 'git {gitArguments}' in {delaySeconds}s (attempt {attempt + 1}/{MaxRemoteRetries})...",
            ct);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
    }

    private async Task ExecuteNoOutputAsync(string gitArguments, CancellationToken ct)
    {
        var errorBuilder = new StringBuilder();
        int exitCode = await _processRunner.ExecuteAsync(
            "git",
            gitArguments,
            _projectContext.WorkingDirectory,
            null,
            msg => errorBuilder.AppendLine(msg),
            ct);

        if (exitCode != 0)
        {
            string errorDetails = errorBuilder.ToString().Trim();
            throw new InvalidOperationException($"Git command 'git {gitArguments}' failed with exit code {exitCode}. Error Details: {errorDetails}");
        }
    }
}