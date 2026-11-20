namespace Instancium.Publisher.Domain.Versioning;

/// <summary>Well-known strategy ids of the built-in catalog.</summary>
public static class KnownVersionBumpStrategies
{
    public const string Patch = "patch";
    public const string Minor = "minor";
    public const string Major = "major";
    public const string PreReleaseIteration = "pre-release-iteration";
    public const string NewPreReleasePatch = "new-pre-release-patch";
    public const string NewPreReleaseMinor = "new-pre-release-minor";
    public const string NewPreReleaseMajor = "new-pre-release-major";
    public const string PromoteToRelease = "promote-to-release";
    public const string Custom = "custom";
}
