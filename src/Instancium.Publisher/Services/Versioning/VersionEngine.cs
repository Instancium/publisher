

using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Instancium.Publisher.Services.Versioning;

public sealed class VersionEngine : IVersionEngine
{
    public SemanticVersion Parse(string rawVersion) => SemanticVersion.Parse(rawVersion);

    public SemanticVersion CalculateNext(
        SemanticVersion current,
        VersionBumpStrategy strategy,
        string? preReleaseTag = "rc",
        string? customVersionString = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        string tag = string.IsNullOrWhiteSpace(preReleaseTag) ? "rc" : preReleaseTag.Trim();
        return strategy switch
        {
            VersionBumpStrategy.Patch => current.IsPreRelease
                ? new SemanticVersion(current.Major, current.Minor, current.Patch)
                : new SemanticVersion(current.Major, current.Minor, current.Patch + 1),
            VersionBumpStrategy.Minor => new SemanticVersion(current.Major, current.Minor + 1, 0),
            VersionBumpStrategy.Major => new SemanticVersion(current.Major + 1, 0, 0),
            VersionBumpStrategy.PreReleaseIteration => current.IsPreRelease
                ? new SemanticVersion(current.Major, current.Minor, current.Patch, current.PreReleaseTag ?? tag, (current.PreReleaseIteration ?? 1) + 1)
                : new SemanticVersion(current.Major, current.Minor, current.Patch + 1, tag, 1),
            VersionBumpStrategy.NewPreReleasePatch => new SemanticVersion(
                current.Major,
                current.Minor,
                current.IsPreRelease ? current.Patch : current.Patch + 1,
                tag,
                1),
            VersionBumpStrategy.NewPreReleaseMinor => new SemanticVersion(current.Major, current.Minor + 1, 0, tag, 1),
            VersionBumpStrategy.NewPreReleaseMajor => new SemanticVersion(current.Major + 1, 0, 0, tag, 1),
            VersionBumpStrategy.PromoteToRelease => current.IsPreRelease
                ? new SemanticVersion(current.Major, current.Minor, current.Patch)
                : current,
            VersionBumpStrategy.Custom => !string.IsNullOrWhiteSpace(customVersionString)
                ? Parse(customVersionString)
                : throw new ArgumentException("Custom version string required for Custom strategy.", nameof(customVersionString)),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), $"Unsupported strategy: {strategy}")
        };
    }

    public async Task<ProjectVersionSnapshot> ReadProjectVersionAsync(string projectPath, CancellationToken ct = default)
    {
        XElement? versionElement = await ReadVersionElementAsync(projectPath, ct);
        SemanticVersion? declaredVersion = string.IsNullOrWhiteSpace(versionElement?.Value)
            ? null
            : Parse(versionElement.Value);
        return new ProjectVersionSnapshot(projectPath, declaredVersion);
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
            await WriteVersionToProjectInternalAsync(projectPath, version, ct);
        }
    }

    private static async Task<XElement?> ReadVersionElementAsync(string projectPath, CancellationToken ct)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: '{projectPath}'.", projectPath);
        }
        await using var stream = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, ct);
        return document.Root?.Descendants("Version").FirstOrDefault();
    }

    private static async Task WriteVersionToProjectInternalAsync(
        string projectPath,
        SemanticVersion version,
        CancellationToken ct)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: '{projectPath}'.", projectPath);
        }
        XDocument document;
        await using (var readStream = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        {
            document = await XDocument.LoadAsync(readStream, LoadOptions.None, ct);
        }
        var root = document.Root ?? throw new InvalidOperationException("Project file contains no XML root.");
        var versionElement = root.Descendants("Version").FirstOrDefault();
        if (versionElement != null)
        {
            versionElement.Value = version.ToString();
        }
        else
        {
            var propertyGroup = root.Element("PropertyGroup");
            if (propertyGroup == null)
            {
                propertyGroup = new XElement("PropertyGroup");
                root.AddFirst(propertyGroup);
            }
            propertyGroup.Add(new XElement("Version", version.ToString()));
        }
        await using var writeStream = new FileStream(projectPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await document.SaveAsync(writeStream, SaveOptions.None, ct);
    }
}
