namespace Instancium.Publisher.Pipeline.Steps;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using Instancium.Publisher.State;

/// <summary>
/// Applies repository-wide versioning: resolves the repository version from all project snapshots,
/// calculates the next version and writes it into every project of the repository.
/// The calculated plan is journaled into pipeline state before any file mutation,
/// making the step idempotent on resume after a crash.
/// </summary>
public sealed class BumpVersionStep
{
    private readonly IVersionEngine _engine;
    private readonly IPipelineStateManager _stateManager;

    public BumpVersionStep(IVersionEngine engine, IPipelineStateManager stateManager)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public async Task ExecuteAsync(
        PipelineContext context,
        IReadOnlyList<ProjectVersionSnapshot> snapshots,
        VersionBumpStrategy? strategy,
        string? customVersionString = null,
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
            if (strategy is null)
            {
                throw new InvalidOperationException("Version bump strategy is required when the pipeline context has no planned version.");
            }

            // strategy is guaranteed non-null here after the check above.
            VersionBumpStrategy strategyValue = strategy.Value;

            SemanticVersion? declaredVersion = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);
            context.CurrentVersion = declaredVersion;
            context.NewVersion = declaredVersion is null
                ? RepositoryVersionPolicy.CalculateInitialVersion(strategyValue, customVersionString: customVersionString)
                : _engine.CalculateNext(declaredVersion, strategyValue, customVersionString: customVersionString);
            await _stateManager.SaveAsync(context);
        }

        IReadOnlyList<string> projectPaths = snapshots.Select(snapshot => snapshot.ProjectPath).ToList();
        await _engine.WriteVersionToProjectsAsync(projectPaths, context.NewVersion, ct);
    }
}