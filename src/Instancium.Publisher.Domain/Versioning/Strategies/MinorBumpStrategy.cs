namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Increments the minor component and resets patch to zero.</summary>
public sealed class MinorBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.Minor;
    public string DisplayName => "Minor";
    public string Description => "Increment minor component and reset patch to zero.";
    public bool RequiresCustomVersion => false;

    public SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (current is null)
        {
            return RepositoryVersionPolicy.InitialVersion;
        }

        return new SemanticVersion(current.Major, current.Minor + 1, 0);
    }
}
