


using Instancium.Publisher.Core.Domain;
using Instancium.Publisher.Core.Interfaces;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Instancium.Publisher.Services.Versioning;

public sealed partial class VersionEngine : IVersionEngine
{
    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<tag>[a-zA-Z0-9\-]+)(?:\.(?<iteration>\d+))?)?(?:\+(?<meta>[a-zA-Z0-9\-.]+))?$", RegexOptions.Compiled)]
    private static partial Regex SemVerRegex();

    public SemanticVersion Parse(string rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            throw new ArgumentException("Version string cannot be empty.", nameof(rawVersion));
        }

        string clean = rawVersion.Trim().TrimStart('v', 'V');
        var match = SemVerRegex().Match(clean);

        if (!match.Success)
        {
            throw new FormatException($"String '{rawVersion}' does not conform to valid SemVer 2.0 format.");
        }

        int major = int.Parse(match.Groups["major"].Value);
        int minor = int.Parse(match.Groups["minor"].Value);
        int patch = int.Parse(match.Groups["patch"].Value);

        string? tag = match.Groups["tag"].Success ? match.Groups["tag"].Value : null;
        int? iteration = match.Groups["iteration"].Success ? int.Parse(match.Groups["iteration"].Value) : null;
        string? meta = match.Groups["meta"].Success ? match.Groups["meta"].Value : null;

        return new SemanticVersion(major, minor, patch, tag, iteration, meta);
    }

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
                ? new SemanticVersion(current.Major, current.Minor, current.Patch) // 1.0.1-rc.1 -> 1.0.1
                : new SemanticVersion(current.Major, current.Minor, current.Patch + 1),

            VersionBumpStrategy.Minor => new SemanticVersion(current.Major, current.Minor + 1, 0),

            VersionBumpStrategy.Major => new SemanticVersion(current.Major + 1, 0, 0),

            VersionBumpStrategy.PreReleaseIteration => current.IsPreRelease
                ? new SemanticVersion(current.Major, current.Minor, current.Patch, current.PreReleaseTag ?? tag, (current.PreReleaseIteration ?? 1) + 1)
                : new SemanticVersion(current.Major, current.Minor, current.Patch + 1, tag, 1),

            VersionBumpStrategy.NewPreReleasePatch => new SemanticVersion(
                current.IsPreRelease ? current.Major : current.Major,
                current.IsPreRelease ? current.Minor : current.Minor,
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

    public async Task<SemanticVersion> ReadVersionFromProjectAsync(string projectPath, CancellationToken ct = default)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException($"Project file not found: '{projectPath}'.", projectPath);
        }

        await using var stream = new FileStream(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, ct);

        var versionElement = document.Root?.Descendants("Version").FirstOrDefault();
        if (versionElement == null || string.IsNullOrWhiteSpace(versionElement.Value))
        {
            return new SemanticVersion(1, 0, 0);
        }

        return Parse(versionElement.Value);
    }

    public async Task WriteVersionToProjectAsync(string projectPath, SemanticVersion version, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(version);

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