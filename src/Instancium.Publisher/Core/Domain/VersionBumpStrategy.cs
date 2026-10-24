namespace Instancium.Publisher.Core.Domain
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
