using Instancium.Publisher.Domain.Versioning;

namespace Instancium.Publisher.Application.Abstractions
{
    /// <summary>
    /// Versioning engine operating on repository scope: reads declared versions as snapshots
    /// and writes the resolved repository version into every project of the repository.
    /// </summary>
    public interface IVersionEngine
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
}
