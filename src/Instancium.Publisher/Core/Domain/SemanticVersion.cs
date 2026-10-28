namespace Instancium.Publisher.Core.Domain;

using System;
using System.Globalization;
using System.Text.RegularExpressions;

public sealed partial record SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; init; }
    public int Minor { get; init; }
    public int Patch { get; init; }
    public string? PreReleaseTag { get; init; }
    public int? PreReleaseIteration { get; init; }
    public string? BuildMetadata { get; init; }

    public bool IsPreRelease => !string.IsNullOrWhiteSpace(PreReleaseTag);

    public SemanticVersion(
        int major,
        int minor,
        int patch,
        string? preReleaseTag = null,
        int? preReleaseIteration = null,
        string? buildMetadata = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        if (preReleaseIteration is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preReleaseIteration), "Iteration must be non-negative.");
        }
        Major = major;
        Minor = minor;
        Patch = patch;
        PreReleaseTag = string.IsNullOrWhiteSpace(preReleaseTag) ? null : preReleaseTag.Trim();
        PreReleaseIteration = preReleaseIteration;
        BuildMetadata = string.IsNullOrWhiteSpace(buildMetadata) ? null : buildMetadata.Trim();
    }

    /// <summary>
    /// Parses a SemVer 2.0 string (optionally prefixed with 'v' or 'V').
    /// </summary>
    public static SemanticVersion Parse(string rawVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawVersion);
        string clean = rawVersion.Trim().TrimStart('v', 'V');
        var match = SemVerRegex().Match(clean);
        if (!match.Success)
        {
            throw new FormatException($"String '{rawVersion}' does not conform to valid SemVer 2.0 format.");
        }
        int major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        int minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        int patch = int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
        string? tag = match.Groups["tag"].Success ? match.Groups["tag"].Value : null;
        int? iteration = match.Groups["iteration"].Success
            ? int.Parse(match.Groups["iteration"].Value, CultureInfo.InvariantCulture)
            : null;
        string? meta = match.Groups["meta"].Success ? match.Groups["meta"].Value : null;
        return new SemanticVersion(major, minor, patch, tag, iteration, meta);
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<tag>[a-zA-Z0-9\-]+)(?:\.(?<iteration>\d+))?)?(?:\+(?<meta>[a-zA-Z0-9\-.]+))?$", RegexOptions.Compiled)]
    private static partial Regex SemVerRegex();

    public override string ToString()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        if (IsPreRelease)
        {
            version += PreReleaseIteration.HasValue
                ? $"-{PreReleaseTag}.{PreReleaseIteration.Value}"
                : $"-{PreReleaseTag}";
        }
        if (!string.IsNullOrEmpty(BuildMetadata))
        {
            version += $"+{BuildMetadata}";
        }
        return version;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        int majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0) return majorComparison;
        int minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0) return minorComparison;
        int patchComparison = Patch.CompareTo(other.Patch);
        if (patchComparison != 0) return patchComparison;
        if (IsPreRelease && !other.IsPreRelease) return -1;
        if (!IsPreRelease && other.IsPreRelease) return 1;
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        int tagComparison = string.Compare(PreReleaseTag, other.PreReleaseTag, StringComparison.OrdinalIgnoreCase);
        if (tagComparison != 0) return tagComparison;
        return Nullable.Compare(PreReleaseIteration, other.PreReleaseIteration);
    }
}