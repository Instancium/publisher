namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Starts a new pre-release on the next major version.</summary>
public sealed class NewPreReleaseMajorBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.NewPreReleaseMajor;
    public string DisplayName => "Pre-Release Major";
    public string Description => "Start a new pre-release for the major component.";
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

        return new SemanticVersion(current.Major + 1, 0, 0, tag, 1);
    }
}
