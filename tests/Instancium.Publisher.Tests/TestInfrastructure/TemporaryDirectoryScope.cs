namespace Instancium.Publisher.Tests.TestInfrastructure;

using System;
using System.IO;

/// <summary>
/// Creates an isolated directory tree under the temp path and removes it recursively on dispose.
/// Every returned path is fully canonical (platform separators, no mixed slashes),
/// so string comparisons against DirectoryInfo/FileInfo FullName values are exact.
/// </summary>
public sealed class TemporaryDirectoryScope : IDisposable
{
    public TemporaryDirectoryScope()
    {
        RootPath = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "instancium-publisher-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateDirectory(string relativePath)
    {
        // Path.GetFullPath canonicalizes separators inside relativePath:
        // Path.Combine alone would preserve forward slashes and break exact path assertions.
        string fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: locked files must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: permission issues must not fail the test run.
        }
    }
}