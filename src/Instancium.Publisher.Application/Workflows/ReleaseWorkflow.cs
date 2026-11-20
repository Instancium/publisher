using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;
using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Domain.Versioning;
using System.Text;

namespace Instancium.Publisher.Application.Workflows;

public sealed record ReleaseRepositoryContext(
    string RepositoryRoot,
    IReadOnlyList<ProjectVersionSnapshot> Snapshots,
    SemanticVersion? CurrentVersion);

public sealed record ReleasePlan(
    IReadOnlyList<ProjectVersionSnapshot> Snapshots,
    SemanticVersion? CurrentVersion,
    SemanticVersion NextVersion,
    string SourceBranch,
    string ReleaseBranch);

public sealed record ReleaseNotesInput(string RangeDescription, string GitLog);

/// <summary>
/// Orchestrates the end-to-end release lifecycle: repository inspection, version planning,
/// branch setup, version bumping, release notes generation, and publication.
/// </summary>
public sealed class ReleaseWorkflow
{
    private readonly IPipelineStateManager _stateManager;
    private readonly IGitService _gitService;
    private readonly BumpVersionStep _bumpStep;
    private readonly MergeAndPublishStep _publishStep;
    private readonly IAiCommitAssistant _aiAssistant;
    private readonly IProjectVersionService _versionService;
    private readonly IVersionBumpService _bumpService;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly IEnvironmentInfoProvider _infoProvider;
    private readonly IProgressReporter _progressReporter;

    public ReleaseWorkflow(
        IPipelineStateManager stateManager,
        IGitService gitService,
        BumpVersionStep bumpStep,
        MergeAndPublishStep publishStep,
        IAiCommitAssistant aiAssistant,
        IProjectVersionService versionService,
        IVersionBumpService bumpService,
        IProjectDiscoveryService projectDiscovery,
        IEnvironmentInfoProvider infoProvider,
        IProgressReporter progressReporter)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _bumpStep = bumpStep ?? throw new ArgumentNullException(nameof(bumpStep));
        _publishStep = publishStep ?? throw new ArgumentNullException(nameof(publishStep));
        _aiAssistant = aiAssistant ?? throw new ArgumentNullException(nameof(aiAssistant));
        _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
        _bumpService = bumpService ?? throw new ArgumentNullException(nameof(bumpService));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
        _infoProvider = infoProvider ?? throw new ArgumentNullException(nameof(infoProvider));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
    }

    public Task<PipelineContext?> LoadStateAsync() => _stateManager.LoadAsync();

    public Task SaveStateAsync(PipelineContext context) => _stateManager.SaveAsync(context);

    public async Task<bool> EnsureReleaseBranchCheckedOutAsync(string releaseBranch, CancellationToken ct = default)
    {
        string currentBranch = await _gitService.GetCurrentBranchAsync(ct);
        if (string.Equals(currentBranch, releaseBranch, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await CheckoutReleaseBranchAsync(releaseBranch, ct);
        return true;
    }

    public async Task<ReleaseRepositoryContext> InspectRepositoryAsync(CancellationToken ct = default)
    {
        string repositoryRoot = await _gitService.GetRepositoryRootAsync(ct);
        IReadOnlyList<string> projectPaths = await _projectDiscovery.DiscoverProjectFilesAsync(repositoryRoot, ct);

        if (projectPaths.Count == 0)
        {
            throw new FileNotFoundException($"No .csproj project files found under repository root: {repositoryRoot}");
        }

        IReadOnlyList<ProjectVersionSnapshot> snapshots = await _versionService.ReadProjectVersionsAsync(projectPaths, ct);
        SemanticVersion? repositoryVersion = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);

        return new ReleaseRepositoryContext(repositoryRoot, snapshots, repositoryVersion);
    }

    public async Task<ReleasePlan> CreatePlanAsync(
        ReleaseRepositoryContext repoContext,
        string strategyId,
        VersionBumpContext? bumpContext = null,
        string releaseBranchPrefix = "release-prep/v",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(repoContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        SemanticVersion nextVersion = _bumpService.CalculateNext(repoContext.CurrentVersion, strategyId, bumpContext);
        string sourceBranch = await _gitService.GetCurrentBranchAsync(ct);
        string releaseBranch = $"{releaseBranchPrefix}{nextVersion}";

        return new ReleasePlan(
            repoContext.Snapshots,
            repoContext.CurrentVersion,
            nextVersion,
            sourceBranch,
            releaseBranch);
    }

    public async Task ApplyVersionAsync(
        PipelineContext state,
        IReadOnlyList<ProjectVersionSnapshot> snapshots,
        string? strategyId,
        VersionBumpContext? bumpContext = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(snapshots);

        await CheckoutReleaseBranchAsync(state.ReleaseBranch, ct);
        await _bumpStep.ExecuteAsync(state, snapshots, strategyId, bumpContext, ct);
        state.CurrentStep = 2;
        await _stateManager.SaveAsync(state);
    }

    public async Task<ReleaseNotesInput> PrepareReleaseNotesContextAsync(PipelineContext state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.NewVersion is null)
        {
            throw new InvalidOperationException("Fatal state error: NewVersion is null at step 2.");
        }

        bool isStableRelease = !state.NewVersion.IsPreRelease;
        string lastTag = await _gitService.GetLatestTagAsync(stableOnly: isStableRelease, ct);

        string rangeDescription = string.IsNullOrWhiteSpace(lastTag)
            ? "last 20 commits (no reachable tags found)"
            : $"{lastTag}..HEAD";

        string gitLog = await _gitService.GetCommitLogAsync(lastTag, ignorePreReleaseTags: isStableRelease, ct);
        return new ReleaseNotesInput(rangeDescription, gitLog);
    }

    public IAsyncEnumerable<string> StreamReleaseNotesAsync(string gitLog, CancellationToken ct = default)
    {
        return _aiAssistant.StreamReleaseNotesAsync(gitLog, ct);
    }

    public async Task<string> FinalizeReleaseNotesAsync(string rawNotes, CancellationToken ct = default)
    {
        string sanitizedNotes = SanitizeReleaseNotes(rawNotes);
        string modelId = await _infoProvider.GetActiveModelIdAsync(ct);
        return AppendSignatureFooter(sanitizedNotes, modelId);
    }

    public Task AdvanceToPublishStepAsync(PipelineContext state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.CurrentStep = 3;
        return _stateManager.SaveAsync(state);
    }

    public async Task PublishAsync(PipelineContext state, string targetBranch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetBranch);

        if (await _gitService.HasUncommittedChangesAsync(ct))
        {
            await _gitService.AddAllAsync(ct);
            await _gitService.CommitAsync($"chore(release): prepare version v{state.NewVersion}", ct);
        }

        await _publishStep.ExecuteAsync(state, targetBranch, ct);
        await _gitService.CheckoutBranchAsync(state.SourceBranch, false, ct);
        await _gitService.DeleteLocalBranchAsync(state.ReleaseBranch, ct);
        await _gitService.DeleteRemoteBranchAsync(state.ReleaseBranch, ct);
        await _stateManager.RollbackAsync();
    }

    private async Task CheckoutReleaseBranchAsync(string branchName, CancellationToken ct)
    {
        try
        {
            await _gitService.CheckoutBranchAsync(branchName, createNew: true, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            await _progressReporter.ReportWarningAsync($"Release branch '{branchName}' already exists. Reusing it.", ct);
            await _gitService.CheckoutBranchAsync(branchName, createNew: false, ct);
        }
    }

    private static string SanitizeReleaseNotes(string rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes))
        {
            return string.Empty;
        }

        var lines = rawNotes.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.StartsWith("```"))
            .Select(l => l.TrimEnd())
            .ToList();

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private string AppendSignatureFooter(string releaseNotes, string modelId)
    {
        string appVersion = _infoProvider.GetApplicationVersion();
        var footer = new StringBuilder();
        footer.AppendLine();
        footer.AppendLine();
        footer.AppendLine("---");
        footer.AppendLine("<p align=\"right\">");
        footer.Append($"<sub>Generated by <b>Instancium.Publisher</b> v{appVersion} \u2022 Model: <code>{modelId}</code></sub>");
        footer.AppendLine();
        footer.Append("</p>");
        return releaseNotes + footer.ToString();
    }
}
