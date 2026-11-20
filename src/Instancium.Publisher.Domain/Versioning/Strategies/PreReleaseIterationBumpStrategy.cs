namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Increments the pre-release iteration, or starts a new patch pre-release from a stable version.</summary>
public sealed class PreReleaseIterationBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.PreReleaseIteration;
    public string DisplayName => "Pre-Release Iteration";
    public string Description => "Increment pre-release iteration or start a new patch pre-release.";
    public bool RequiresCustomVersion => false;

    public SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string tag = string.IsNullOrWhiteSpace(context.PreReleaseTag) ? "rc" : context.PreReleaseTag.Trim();
        if (current is null)
        {
            return new SemanticVersion(
                RepositoryVersionPolicy.InitialVersion.Major,
                RepositoryVersionPolicy.InitialVersion.Minor,
                RepositoryVersionPolicy.InitialVersion.Patch,
                tag,
                1);
        }

        return current.IsPreRelease
            ? new SemanticVersion(current.Major, current.Minor, current.Patch, current.PreReleaseTag ?? tag, (current.PreReleaseIteration ?? 1) + 1)
            : new SemanticVersion(current.Major, current.Minor, current.Patch + 1, tag, 1);
    }
}
