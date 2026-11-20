namespace Instancium.Publisher.Domain.Versioning.Strategies;

/// <summary>Applies an explicit user-provided SemVer 2.0 version.</summary>
public sealed class CustomBumpStrategy : IVersionBumpStrategy
{
    public string Id => KnownVersionBumpStrategies.Custom;
    public string DisplayName => "Custom";
    public string Description => "User defined SemVer 2.0";
    public bool RequiresCustomVersion => true;

    public SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.CustomVersion))
        {
            throw new ArgumentException("Custom version string required for Custom strategy.", nameof(context));
        }

        return SemanticVersion.Parse(context.CustomVersion);
    }
}
