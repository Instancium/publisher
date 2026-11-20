using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;
using System.IO.Enumeration;

namespace Instancium.Publisher.Application.Versioning;

/// <summary>
/// Resolves the matching <see cref="IProjectEcosystem"/> per manifest path and delegates
/// repository-wide version reads and writes.
/// </summary>
public sealed class RepositoryVersionService : IProjectVersionService
{
    private readonly IReadOnlyList<IProjectEcosystem> _ecosystems;

    public RepositoryVersionService(IEnumerable<IProjectEcosystem> ecosystems)
    {
        ArgumentNullException.ThrowIfNull(ecosystems);
        _ecosystems = ecosystems.ToList();
    }

    public Task<ProjectVersionSnapshot> ReadProjectVersionAsync(string projectPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        IProjectEcosystem ecosystem = ResolveEcosystem(projectPath);
        return ecosystem.ReadVersionAsync(projectPath, ct);
    }

    public async Task<IReadOnlyList<ProjectVersionSnapshot>> ReadProjectVersionsAsync(
        IEnumerable<string> projectPaths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        var snapshots = new List<ProjectVersionSnapshot>();
        foreach (string projectPath in projectPaths)
        {
            snapshots.Add(await ReadProjectVersionAsync(projectPath, ct));
        }

        return snapshots;
    }

    public async Task WriteVersionToProjectsAsync(
        IEnumerable<string> projectPaths,
        SemanticVersion version,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPaths);
        ArgumentNullException.ThrowIfNull(version);
        foreach (string projectPath in projectPaths)
        {
            IProjectEcosystem ecosystem = ResolveEcosystem(projectPath);
            await ecosystem.WriteVersionAsync(projectPath, version, ct);
        }
    }

    private IProjectEcosystem ResolveEcosystem(string manifestPath)
    {
        string fileName = Path.GetFileName(manifestPath);
        foreach (IProjectEcosystem ecosystem in _ecosystems)
        {
            foreach (string pattern in ecosystem.ManifestFilePatterns)
            {
                if (FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
                {
                    return ecosystem;
                }
            }
        }

        throw new InvalidOperationException($"No registered project ecosystem matches manifest '{manifestPath}'.");
    }
}
