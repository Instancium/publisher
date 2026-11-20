namespace Instancium.Publisher.Domain.Versioning;

/// <summary>
/// Extension point for version bump strategies. A null current version means the repository
/// has no declared version yet (initial versioning scenario).
/// </summary>
public interface IVersionBumpStrategy
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    bool RequiresCustomVersion { get; }
    SemanticVersion CalculateNext(SemanticVersion? current, VersionBumpContext context);
}
