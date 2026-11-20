using Instancium.Publisher.Application.Abstractions;

namespace Instancium.Publisher.Infrastructure.Versioning;

/// <summary>
/// File-system discovery of project manifests participating in repository-wide versioning
/// across all registered <see cref="IProjectEcosystem"/> implementations.
/// </summary>
public sealed class RepositoryProjectScanner : IProjectDiscoveryService
{
    private readonly IReadOnlyList<string> _patterns;
    private readonly HashSet<string> _excludedDirectoryNames;

    public RepositoryProjectScanner(IEnumerable<IProjectEcosystem> ecosystems)
    {
        ArgumentNullException.ThrowIfNull(ecosystems);
        var ecosystemList = ecosystems.ToList();
        _patterns = ecosystemList
            .SelectMany(e => e.ManifestFilePatterns)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _excludedDirectoryNames = new HashSet<string>(
            ecosystemList.SelectMany(e => e.ExcludedDirectoryNames),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<string>> DiscoverProjectFilesAsync(string rootDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return await Task.Run(() => Discover(rootDirectory, ct), ct);
    }

    private IReadOnlyList<string> Discover(string rootDirectory, CancellationToken ct)
    {
        var root = new DirectoryInfo(rootDirectory);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Repository root directory not found: '{rootDirectory}'.");
        }

        var projectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectProjectFiles(root, projectFiles, ct);
        var sorted = projectFiles.ToList();
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    private void CollectProjectFiles(DirectoryInfo directory, HashSet<string> accumulator, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            foreach (string pattern in _patterns)
            {
                foreach (FileInfo file in directory.EnumerateFiles(pattern))
                {
                    accumulator.Add(file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            return;
        }

        try
        {
            foreach (DirectoryInfo subDirectory in directory.EnumerateDirectories())
            {
                if (_excludedDirectoryNames.Contains(subDirectory.Name))
                {
                    continue;
                }

                if ((subDirectory.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                {
                    continue;
                }

                CollectProjectFiles(subDirectory, accumulator, ct);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
        {
            // Inaccessible subtree is intentionally skipped: versioning must not fail on tooling folders.
        }
    }
}
