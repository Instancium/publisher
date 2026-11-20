namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Tests.TestInfrastructure;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Verifies repository-wide version reading/writing across multiple projects
/// and the bump strategy matrix.
/// </summary>
public sealed class RepositoryVersionServiceTests
{
    private readonly IProjectVersionService _service = VersioningTestFactory.CreateVersionService();

    [Fact]
    public async Task ReadProjectVersionsAsync_MultipleProjects_ReturnsSnapshotPerProject()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "1.0.0");
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: null);

        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _service.ReadProjectVersionsAsync(new[] { projectA, projectB });

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("1.0.0", snapshots[0].DeclaredVersion?.ToString());
        Assert.Null(snapshots[1].DeclaredVersion);
    }

    [Fact]
    public async Task WriteVersionToProjectsAsync_UpdatesEveryProjectAndAddsMissingElement()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "1.0.0");
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: "0.9.0");
        string projectC = CsprojFileFactory.Create(scope.CreateDirectory("src/C"), "C.csproj", version: null);
        var targetVersion = SemanticVersion.Parse("2.0.0");

        await _service.WriteVersionToProjectsAsync(new[] { projectA, projectB, projectC }, targetVersion);

        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectA));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectB));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectC));
    }

    [Theory]
    [InlineData("1.0.0", KnownVersionBumpStrategies.Patch, "1.0.1")]
    [InlineData("1.0.1-rc.1", KnownVersionBumpStrategies.Patch, "1.0.1")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.Minor, "1.1.0")]
    [InlineData("1.0.9", KnownVersionBumpStrategies.Minor, "1.1.0")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.Major, "2.0.0")]
    [InlineData("1.0.1-rc.1", KnownVersionBumpStrategies.PreReleaseIteration, "1.0.1-rc.2")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.PreReleaseIteration, "1.0.1-rc.1")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.NewPreReleasePatch, "1.0.1-rc.1")]
    [InlineData("1.0.1-rc.1", KnownVersionBumpStrategies.NewPreReleasePatch, "1.0.1-rc.1")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.NewPreReleaseMinor, "1.1.0-rc.1")]
    [InlineData("1.0.0", KnownVersionBumpStrategies.NewPreReleaseMajor, "2.0.0-rc.1")]
    [InlineData("1.0.1-rc.2", KnownVersionBumpStrategies.PromoteToRelease, "1.0.1")]
    [InlineData("1.0.1", KnownVersionBumpStrategies.PromoteToRelease, "1.0.1")]
    public void CalculateNext_AppliesStrategyMatrix(string currentRaw, string strategyId, string expected)
    {
        var bumpService = VersioningTestFactory.CreateBumpService();
        SemanticVersion next = bumpService.CalculateNext(SemanticVersion.Parse(currentRaw), strategyId);

        Assert.Equal(expected, next.ToString());
    }

    [Fact]
    public void CalculateNext_CustomStrategy_UsesProvidedString()
    {
        var bumpService = VersioningTestFactory.CreateBumpService();
        SemanticVersion next = bumpService.CalculateNext(
            SemanticVersion.Parse("1.0.0"),
            KnownVersionBumpStrategies.Custom,
            new VersionBumpContext(CustomVersion: "v4.5.6"));

        Assert.Equal("4.5.6", next.ToString());
    }

    [Fact]
    public void CalculateNext_CustomStrategyWithoutString_ThrowsArgumentException()
    {
        var bumpService = VersioningTestFactory.CreateBumpService();
        Assert.Throws<ArgumentException>(() =>
            bumpService.CalculateNext(SemanticVersion.Parse("1.0.0"), KnownVersionBumpStrategies.Custom));
    }
}
