namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Increments the major component and resets minor and patch to zero.</summary>
public sealed class MajorBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.Major;
    public string DisplayName => "Major";
    public string Description => "Increment major component and reset minor and patch to zero.";
    public bool RequiresCustomVersion => false;

    public SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (current is null)
        {
            return RepositoryVersionPolicy.InitialVersion;
        }

        return new SemanticVersion(current.Major + 1, 0, 0);
    }
}
