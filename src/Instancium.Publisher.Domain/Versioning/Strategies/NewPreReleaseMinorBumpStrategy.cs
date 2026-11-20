namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Starts a new pre-release on the next minor version.</summary>
public sealed class NewPreReleaseMinorBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.NewPreReleaseMinor;
    public string DisplayName => "Pre-Release Minor";
    public string Description => "Start a new pre-release for the minor component.";
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

        return new SemanticVersion(current.Major, current.Minor + 1, 0, tag, 1);
    }
}
