namespace Instancium.Publisher.Tests.Pipeline;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Pipeline.Steps;
using Instancium.Publisher.Services.Versioning;
using Instancium.Publisher.State;
using Instancium.Publisher.Tests.TestInfrastructure;
using Xunit;

/// <summary>
/// Verifies repository-wide version bumping end-to-end on real project files:
/// initial versioning baseline, bump from the highest declared version, alignment of every
/// project, journaling before mutation and idempotent resume after an interrupted release.
/// </summary>
public sealed class BumpVersionStepTests
{
    private readonly VersionEngine _engine = new();

    [Fact]
    public async Task ExecuteAsync_NoDeclaredVersions_StampsBaselineIntoEveryProject()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: null);
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: null);
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext { CurrentStep = 1 };
        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA, projectB });

        await step.ExecuteAsync(context, snapshots, VersionBumpStrategy.Patch);

        Assert.Null(context.CurrentVersion);
        Assert.Equal("1.0.0", context.NewVersion?.ToString());
        Assert.Equal("1.0.0", CsprojFileFactory.ReadVersionElementValue(projectA));
        Assert.Equal("1.0.0", CsprojFileFactory.ReadVersionElementValue(projectB));
        Assert.Equal(1, stateManager.SaveCount);
        Assert.Equal("1.0.0", stateManager.SavedContexts[0].NewVersion?.ToString());
        Assert.Equal(1, stateManager.SavedContexts[0].CurrentStep);
    }

    [Fact]
    public async Task ExecuteAsync_MixedDeclaredVersions_BumpsFromHighestAndAlignsAllProjects()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "1.2.0");
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: "1.5.3");
        string projectC = CsprojFileFactory.Create(scope.CreateDirectory("tests/C"), "C.csproj", version: null);
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext { CurrentStep = 1 };
        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA, projectB, projectC });

        await step.ExecuteAsync(context, snapshots, VersionBumpStrategy.Minor);

        Assert.Equal("1.5.3", context.CurrentVersion?.ToString());
        Assert.Equal("1.6.0", context.NewVersion?.ToString());
        Assert.Equal("1.6.0", CsprojFileFactory.ReadVersionElementValue(projectA));
        Assert.Equal("1.6.0", CsprojFileFactory.ReadVersionElementValue(projectB));
        Assert.Equal("1.6.0", CsprojFileFactory.ReadVersionElementValue(projectC));
    }

    [Fact]
    public async Task ExecuteAsync_InitialPreReleaseStrategy_StampsTaggedBaseline()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: null);
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext { CurrentStep = 1 };
        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA });

        await step.ExecuteAsync(context, snapshots, VersionBumpStrategy.NewPreReleaseMajor);

        Assert.Equal("1.0.0-rc.1", context.NewVersion?.ToString());
        Assert.Equal("1.0.0-rc.1", CsprojFileFactory.ReadVersionElementValue(projectA));
    }

    [Fact]
    public async Task ExecuteAsync_ResumedWithPlannedVersion_CompletesWritesWithoutRebump()
    {
        using var scope = new TemporaryDirectoryScope();
        // Simulates a crash after partial write: two projects already carry the planned version.
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "2.0.0");
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: "2.0.0");
        string projectC = CsprojFileFactory.Create(scope.CreateDirectory("src/C"), "C.csproj", version: "1.9.0");
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext
        {
            CurrentStep = 1,
            CurrentVersion = SemanticVersion.Parse("1.9.0"),
            NewVersion = SemanticVersion.Parse("2.0.0")
        };
        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA, projectB, projectC });

        await step.ExecuteAsync(context, snapshots, strategy: null);

        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectA));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectB));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectC));
        Assert.Equal("2.0.0", context.NewVersion?.ToString());
        Assert.Equal(0, stateManager.SaveCount);
    }

    [Fact]
    public async Task ExecuteAsync_NoPlannedVersionAndNoStrategy_ThrowsInvalidOperationException()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "1.0.0");
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext { CurrentStep = 1 };
        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => step.ExecuteAsync(context, snapshots, strategy: null));
    }

    [Fact]
    public async Task ExecuteAsync_EmptySnapshots_ThrowsArgumentException()
    {
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);
        var context = new PipelineContext { CurrentStep = 1 };

        await Assert.ThrowsAsync<ArgumentException>(
            () => step.ExecuteAsync(context, Array.Empty<ProjectVersionSnapshot>(), VersionBumpStrategy.Patch));
    }

    [Fact]
    public async Task ExecuteAsync_NullContext_ThrowsArgumentNullException()
    {
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BumpVersionStep(_engine, stateManager);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => step.ExecuteAsync(null!, Array.Empty<ProjectVersionSnapshot>(), VersionBumpStrategy.Patch));
    }
}
