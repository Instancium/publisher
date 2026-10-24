namespace Instancium.Publisher.Core.Domain;

using System;

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

        // SemVer rule: 1.0.0-rc is lower precedence than 1.0.0
        if (IsPreRelease && !other.IsPreRelease) return -1;
        if (!IsPreRelease && other.IsPreRelease) return 1;
        if (!IsPreRelease && !other.IsPreRelease) return 0;

        int tagComparison = string.Compare(PreReleaseTag, other.PreReleaseTag, StringComparison.OrdinalIgnoreCase);
        if (tagComparison != 0) return tagComparison;

        return Nullable.Compare(PreReleaseIteration, other.PreReleaseIteration);
    }
}