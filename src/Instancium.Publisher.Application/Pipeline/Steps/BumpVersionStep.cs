namespace Instancium.Publisher.Application.Pipeline.Steps;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Application.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Applies repository-wide versioning: resolves the repository version from all project snapshots,
/// calculates the next version and writes it into every project of the repository.
/// The calculated plan is journaled into pipeline state before any file mutation,
/// making the step idempotent on resume after a crash.
/// </summary>
public sealed class BumpVersionStep
{
    private readonly IProjectVersionService _versionService;
    private readonly IVersionBumpService _bumpService;
    private readonly IPipelineStateManager _stateManager;

    public BumpVersionStep(
        IProjectVersionService versionService,
        IVersionBumpService bumpService,
        IPipelineStateManager stateManager)
    {
        _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
        _bumpService = bumpService ?? throw new ArgumentNullException(nameof(bumpService));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public async Task ExecuteAsync(
        PipelineContext context,
        IReadOnlyList<ProjectVersionSnapshot> snapshots,
        string? strategyId,
        VersionBumpContext? bumpContext = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0)
        {
            throw new ArgumentException("At least one project snapshot is required for repository versioning.", nameof(snapshots));
        }

        if (context.NewVersion is null)
        {
            if (string.IsNullOrWhiteSpace(strategyId))
            {
                throw new InvalidOperationException("Version bump strategy is required when the pipeline context has no planned version.");
            }

            SemanticVersion? declaredVersion = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);
            context.CurrentVersion = declaredVersion;
            context.NewVersion = _bumpService.CalculateNext(declaredVersion, strategyId, bumpContext);
            await _stateManager.SaveAsync(context);
        }

        IReadOnlyList<string> projectPaths = snapshots.Select(snapshot => snapshot.ProjectPath).ToList();
        await _versionService.WriteVersionToProjectsAsync(projectPaths, context.NewVersion, ct);
    }
}