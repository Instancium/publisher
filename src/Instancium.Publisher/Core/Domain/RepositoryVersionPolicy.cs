namespace Instancium.Publisher.Core.Domain;

using System;
using System.Collections.Generic;

/// <summary>
/// Domain policy that defines repository-wide versioning rules.
/// A release is published for the whole repository: the repository version is the highest
/// version declared among its projects, and every project is aligned to the new version on release.
/// </summary>
public static class RepositoryVersionPolicy
{
    /// <summary>
    /// Baseline version applied when no project in the repository declares a version yet.
    /// </summary>
    public static SemanticVersion InitialVersion { get; } = new(1, 0, 0);

    /// <summary>
    /// Resolves the current repository version as the highest declared version.
    /// Returns null when no project declares a version (initial versioning scenario).
    /// </summary>
    public static SemanticVersion? TryResolveCurrentVersion(IReadOnlyCollection<ProjectVersionSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        SemanticVersion? highest = null;
        foreach (ProjectVersionSnapshot snapshot in snapshots)
        {
            if (snapshot.DeclaredVersion is null)
            {
                continue;
            }
            if (highest is null || snapshot.DeclaredVersion.CompareTo(highest) > 0)
            {
                highest = snapshot.DeclaredVersion;
            }
        }
        return highest;
    }

    /// <summary>
    /// Calculates the very first version for a repository without any declared version.
    /// Stable strategies yield the 1.0.0 baseline; pre-release strategies yield 1.0.0-tag.1.
    /// </summary>
    public static SemanticVersion CalculateInitialVersion(
        VersionBumpStrategy strategy,
        string? preReleaseTag = "rc",
        string? customVersionString = null)
    {
        string tag = string.IsNullOrWhiteSpace(preReleaseTag) ? "rc" : preReleaseTag.Trim();
        return strategy switch
        {
            VersionBumpStrategy.PreReleaseIteration
                or VersionBumpStrategy.NewPreReleasePatch
                or VersionBumpStrategy.NewPreReleaseMinor
                or VersionBumpStrategy.NewPreReleaseMajor =>
                new SemanticVersion(InitialVersion.Major, InitialVersion.Minor, InitialVersion.Patch, tag, 1),
            VersionBumpStrategy.Custom => SemanticVersion.Parse(
                string.IsNullOrWhiteSpace(customVersionString)
                    ? throw new ArgumentException("Custom version string required for Custom strategy.", nameof(customVersionString))
                    : customVersionString),
            _ => InitialVersion
        };
    }

    /// <summary>
    /// Counts projects whose declared version differs from the resolved repository version
    /// (including projects without any declared version). These projects will be realigned on release.
    /// </summary>
    public static int CountDivergentProjects(
        IReadOnlyCollection<ProjectVersionSnapshot> snapshots,
        SemanticVersion? repositoryVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        int divergent = 0;
        foreach (ProjectVersionSnapshot snapshot in snapshots)
        {
            bool equal = snapshot.DeclaredVersion is null
                ? repositoryVersion is null
                : repositoryVersion is not null && snapshot.DeclaredVersion.CompareTo(repositoryVersion) == 0;
            if (!equal)
            {
                divergent++;
            }
        }
        return divergent;
    }
}
