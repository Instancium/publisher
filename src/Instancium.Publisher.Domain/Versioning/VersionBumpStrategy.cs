namespace Instancium.Publisher.Domain.Versioning
{
    public enum VersionBumpStrategy
    {
        Patch,
        Minor,
        Major,
        PreReleaseIteration,
        NewPreReleasePatch,
        NewPreReleaseMinor,
        NewPreReleaseMajor,
        PromoteToRelease,
        Custom
    }
}
