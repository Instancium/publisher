namespace Instancium.Publisher.Tests.Domain;

using System;
using System.Collections.Generic;
using Instancium.Publisher.Core.Domain;
using Xunit;

/// <summary>
/// Verifies repository-wide versioning rules: the repository version is the highest declared
/// version among projects, missing versions mean initial versioning with the 1.0.0 baseline,
/// and divergent projects are counted for realignment on release.
/// </summary>
public sealed class RepositoryVersionPolicyTests
{
    [Fact]
    public void TryResolveCurrentVersion_NoDeclaredVersions_ReturnsNull()
    {
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", null),
            Snapshot("/repo/src/B/B.csproj", null)
        };

        SemanticVersion? resolved = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);

        Assert.Null(resolved);
    }

    [Fact]
    public void TryResolveCurrentVersion_MixedDeclaredVersions_ReturnsNumericHighest()
    {
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", "1.2.0"),
            Snapshot("/repo/src/B/B.csproj", null),
            Snapshot("/repo/src/C/C.csproj", "1.10.0"),
            Snapshot("/repo/src/D/D.csproj", "1.9.3")
        };

        SemanticVersion? resolved = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);

        // 1.10.0 must win over 1.9.3: numeric comparison, not lexical.
        Assert.Equal("1.10.0", resolved?.ToString());
    }

    [Fact]
    public void TryResolveCurrentVersion_PreReleaseIsLowerThanRelease()
    {
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", "2.0.0-rc.1"),
            Snapshot("/repo/src/B/B.csproj", "1.9.9")
        };

        SemanticVersion? resolved = RepositoryVersionPolicy.TryResolveCurrentVersion(snapshots);

        Assert.Equal("2.0.0-rc.1", resolved?.ToString());
    }

    [Theory]
    [InlineData(VersionBumpStrategy.Patch)]
    [InlineData(VersionBumpStrategy.Minor)]
    [InlineData(VersionBumpStrategy.Major)]
    [InlineData(VersionBumpStrategy.PromoteToRelease)]
    public void CalculateInitialVersion_StableStrategies_ReturnBaseline(VersionBumpStrategy strategy)
    {
        SemanticVersion initial = RepositoryVersionPolicy.CalculateInitialVersion(strategy);

        Assert.Equal("1.0.0", initial.ToString());
    }

    [Theory]
    [InlineData(VersionBumpStrategy.PreReleaseIteration)]
    [InlineData(VersionBumpStrategy.NewPreReleasePatch)]
    [InlineData(VersionBumpStrategy.NewPreReleaseMinor)]
    [InlineData(VersionBumpStrategy.NewPreReleaseMajor)]
    public void CalculateInitialVersion_PreReleaseStrategies_ReturnTaggedBaseline(VersionBumpStrategy strategy)
    {
        SemanticVersion initial = RepositoryVersionPolicy.CalculateInitialVersion(strategy);

        Assert.Equal("1.0.0-rc.1", initial.ToString());
    }

    [Fact]
    public void CalculateInitialVersion_CustomPreReleaseTag_UsesProvidedTag()
    {
        SemanticVersion initial = RepositoryVersionPolicy.CalculateInitialVersion(
            VersionBumpStrategy.NewPreReleaseMinor,
            preReleaseTag: "beta");

        Assert.Equal("1.0.0-beta.1", initial.ToString());
    }

    [Fact]
    public void CalculateInitialVersion_CustomStrategy_ParsesProvidedString()
    {
        SemanticVersion initial = RepositoryVersionPolicy.CalculateInitialVersion(
            VersionBumpStrategy.Custom,
            customVersionString: "v3.5.7");

        Assert.Equal("3.5.7", initial.ToString());
    }

    [Fact]
    public void CalculateInitialVersion_CustomStrategyWithoutString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            RepositoryVersionPolicy.CalculateInitialVersion(VersionBumpStrategy.Custom));
    }

    [Fact]
    public void CountDivergentProjects_AllAlignedToRepositoryVersion_ReturnsZero()
    {
        SemanticVersion repositoryVersion = SemanticVersion.Parse("1.4.0");
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", "1.4.0"),
            Snapshot("/repo/src/B/B.csproj", "1.4.0")
        };

        int divergent = RepositoryVersionPolicy.CountDivergentProjects(snapshots, repositoryVersion);

        Assert.Equal(0, divergent);
    }

    [Fact]
    public void CountDivergentProjects_MissingAndDifferentVersions_CountsThem()
    {
        SemanticVersion repositoryVersion = SemanticVersion.Parse("1.4.0");
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", "1.4.0"),
            Snapshot("/repo/src/B/B.csproj", null),
            Snapshot("/repo/src/C/C.csproj", "1.3.9")
        };

        int divergent = RepositoryVersionPolicy.CountDivergentProjects(snapshots, repositoryVersion);

        Assert.Equal(2, divergent);
    }

    [Fact]
    public void CountDivergentProjects_NoRepositoryVersionAndNoDeclaredVersions_ReturnsZero()
    {
        var snapshots = new List<ProjectVersionSnapshot>
        {
            Snapshot("/repo/src/A/A.csproj", null),
            Snapshot("/repo/src/B/B.csproj", null)
        };

        int divergent = RepositoryVersionPolicy.CountDivergentProjects(snapshots, repositoryVersion: null);

        Assert.Equal(0, divergent);
    }

    private static ProjectVersionSnapshot Snapshot(string projectPath, string? version) =>
        new(projectPath, version is null ? null : SemanticVersion.Parse(version));
}
