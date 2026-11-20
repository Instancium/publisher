namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Versioning;
using Instancium.Publisher.Tests.TestInfrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit;

/// <summary>
/// Verifies the version engine against real SDK-style project files on disk:
/// snapshot reading (missing version must stay null, not default to 1.0.0),
/// repository-wide writing and the bump strategy matrix.
/// </summary>
public sealed class VersionEngineTests
{
    private readonly VersionEngine _engine = new();

    [Fact]
    public async Task ReadProjectVersionAsync_DeclaredVersion_ReturnsSnapshotWithVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.Create(scope.RootPath, "Sample.csproj", version: "2.3.4-rc.2");

        ProjectVersionSnapshot snapshot = await _engine.ReadProjectVersionAsync(projectPath);

        Assert.Equal(projectPath, snapshot.ProjectPath);
        Assert.True(snapshot.HasDeclaredVersion);
        Assert.Equal("2.3.4-rc.2", snapshot.DeclaredVersion?.ToString());
    }

    [Fact]
    public async Task ReadProjectVersionAsync_MissingVersionElement_ReturnsNullDeclaredVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.Create(scope.RootPath, "Sample.csproj", version: null);

        ProjectVersionSnapshot snapshot = await _engine.ReadProjectVersionAsync(projectPath);

        // Regression guard: absence of <Version> must NOT silently become 1.0.0.
        Assert.False(snapshot.HasDeclaredVersion);
        Assert.Null(snapshot.DeclaredVersion);
    }

    [Fact]
    public async Task ReadProjectVersionAsync_MissingFile_ThrowsFileNotFoundException()
    {
        using var scope = new TemporaryDirectoryScope();
        string missingPath = Path.Combine(scope.RootPath, "Missing.csproj");

        await Assert.ThrowsAsync<FileNotFoundException>(() => _engine.ReadProjectVersionAsync(missingPath));
    }

    [Fact]
    public async Task ReadProjectVersionsAsync_MultipleProjects_ReturnsSnapshotPerProject()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectA = CsprojFileFactory.Create(scope.CreateDirectory("src/A"), "A.csproj", version: "1.0.0");
        string projectB = CsprojFileFactory.Create(scope.CreateDirectory("src/B"), "B.csproj", version: null);

        IReadOnlyList<ProjectVersionSnapshot> snapshots =
            await _engine.ReadProjectVersionsAsync(new[] { projectA, projectB });

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

        await _engine.WriteVersionToProjectsAsync(new[] { projectA, projectB, projectC }, targetVersion);

        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectA));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectB));
        Assert.Equal("2.0.0", CsprojFileFactory.ReadVersionElementValue(projectC));
    }

    [Fact]
    public async Task WriteVersionToProjectsAsync_ProjectWithoutPropertyGroup_AddsPropertyGroupWithVersion()
    {
        using var scope = new TemporaryDirectoryScope();
        string projectPath = CsprojFileFactory.CreateWithoutPropertyGroup(scope.RootPath, "Bare.csproj");
        var targetVersion = SemanticVersion.Parse("3.1.4");

        await _engine.WriteVersionToProjectsAsync(new[] { projectPath }, targetVersion);

        Assert.Equal("3.1.4", CsprojFileFactory.ReadVersionElementValue(projectPath));
        Assert.True(CsprojFileFactory.HasPropertyGroup(projectPath));
        XDocument document = XDocument.Load(projectPath);
        XElement? propertyGroup = document.Root?.Element("PropertyGroup");
        Assert.Equal("3.1.4", propertyGroup?.Element("Version")?.Value);
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
