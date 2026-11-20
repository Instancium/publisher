using Instancium.Publisher.Domain.Versioning;

namespace Instancium.Publisher.Application.Abstractions;

/// <summary>
/// Versioning service operating on repository scope: reads declared versions as snapshots
/// and writes the resolved repository version into every project manifest of the repository.
/// </summary>
public interface IProjectVersionService
{
    Task<ProjectVersionSnapshot> ReadProjectVersionAsync(string projectPath, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectVersionSnapshot>> ReadProjectVersionsAsync(
        IEnumerable<string> projectPaths,
        CancellationToken ct = default);

    Task WriteVersionToProjectsAsync(
        IEnumerable<string> projectPaths,
        SemanticVersion version,
        CancellationToken ct = default);
}
