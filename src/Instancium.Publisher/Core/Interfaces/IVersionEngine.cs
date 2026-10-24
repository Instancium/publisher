using Instancium.Publisher.Core.Domain;

namespace Instancium.Publisher.Core.Interfaces
{
    public interface IVersionEngine
    {
        SemanticVersion Parse(string rawVersion);
        SemanticVersion CalculateNext(
            SemanticVersion current,
            VersionBumpStrategy strategy,
            string? preReleaseTag = "rc",
            string? customVersionString = null);

        Task<SemanticVersion> ReadVersionFromProjectAsync(string projectPath, CancellationToken ct = default);
        Task WriteVersionToProjectAsync(string projectPath, SemanticVersion version, CancellationToken ct = default);
    }
}
