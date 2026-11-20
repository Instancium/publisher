namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Increments the patch component; finalizes a pre-release to its stable version.</summary>
public sealed class PatchBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.Patch;
    public string DisplayName => "Patch";
    public string Description => "Increment patch; finalize a pre-release to its stable version.";
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
            : new SemanticVersion(current.Major, current.Minor, current.Patch + 1);
    }
}
