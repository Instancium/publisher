namespace Instancium.Publisher.Domain.Versioning;

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
