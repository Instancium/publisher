using Instancium.Publisher.Domain.Versioning;

namespace Instancium.Publisher.Application.Abstractions;

/// <summary>
/// Extension point describing one project ecosystem (MSBuild, npm, ...): how to recognize its
/// manifest files, which directories to skip, and how to read/write the declared version.
/// </summary>
public interface IProjectEcosystem
{
    string Id { get; }
    IReadOnlyList<string> ManifestFilePatterns { get; }
    IReadOnlyList<string> ExcludedDirectoryNames { get; }
    Task<ProjectVersionSnapshot> ReadVersionAsync(string manifestPath, CancellationToken ct = default);
    Task WriteVersionAsync(string manifestPath, SemanticVersion version, CancellationToken ct = default);
}
