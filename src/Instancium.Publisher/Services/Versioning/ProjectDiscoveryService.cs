namespace Instancium.Publisher.Services.Versioning;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Instancium.Publisher.Core.Interfaces;

/// <summary>
/// File-system discovery of SDK-style projects participating in repository-wide versioning.
/// Build outputs and tooling directories are excluded to avoid versioning generated projects.
/// The traversal runs on the thread pool: the method never throws synchronously,
/// every failure is observed through the awaited task.
/// </summary>
public sealed class ProjectDiscoveryService : IProjectDiscoveryService
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        "node_modules"
    };

    public async Task<IReadOnlyList<string>> DiscoverProjectFilesAsync(string rootDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return await Task.Run(() => Discover(rootDirectory, ct), ct);
    }

    private static IReadOnlyList<string> Discover(string rootDirectory, CancellationToken ct)
    {
        var root = new DirectoryInfo(rootDirectory);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Repository root directory not found: '{rootDirectory}'.");
        }
        var projectFiles = new List<string>();
        CollectProjectFiles(root, projectFiles, ct);
        projectFiles.Sort(StringComparer.OrdinalIgnoreCase);
        return projectFiles;
    }

    private static void CollectProjectFiles(DirectoryInfo directory, List<string> accumulator, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            foreach (FileInfo file in directory.EnumerateFiles("*.csproj"))
            {
                accumulator.Add(file.FullName);
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
                if (ExcludedDirectoryNames.Contains(subDirectory.Name))
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