namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Promotes a pre-release to its stable version without incrementing components.</summary>
public sealed class PromoteToReleaseBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.PromoteToRelease;
    public string DisplayName => "Promote To Release";
    public string Description => "Strip pre-release label to promote to a stable release.";
    public bool RequiresCustomVersion => false;

    public SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (current is null)
        {
            return RepositoryVersionPolicy.InitialVersion;
        }

        return current.IsPreRelease
            ? new SemanticVersion(current.Major, current.Minor, current.Patch)
            : current;
    }
}
