namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Starts a new pre-release on the next patch version (or current patch if already pre-release).</summary>
public sealed class NewPreReleasePatchBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.NewPreReleasePatch;
    public string DisplayName => "Pre-Release Patch";
    public string Description => "Start a new pre-release for the patch component.";
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

        return new SemanticVersion(
            current.Major,
            current.Minor,
            current.IsPreRelease ? current.Patch : current.Patch + 1,
            tag,
            1);
    }
}
