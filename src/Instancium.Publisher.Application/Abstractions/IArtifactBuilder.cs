namespace Instancium.Publisher.Application.Abstractions;

using Instancium.Publisher.Domain.Versioning;

/// <summary>
/// Input parameters for building and packaging release artifacts into an isolated staging directory.
/// </summary>
public sealed record ArtifactBuildRequest(
    string RepositoryRoot,
    string ProductName,
    SemanticVersion Version,
    string OutputDirectory);

/// <summary>
/// Immutable descriptor of a single built or packaged release asset with its verified integrity metadata.
/// </summary>
public sealed record BuiltArtifact(
    string RelativePath,
    string FullPath,
    string Sha256,
    long Size);

/// <summary>
/// Outcome of an artifact build pass. <paramref name="HasManifestConfig"/> distinguishes
/// repositories opting into declarative asset publishing from plain source-only repositories.
/// </summary>
public sealed record ArtifactBuildResult(
    string ProductName,
    IReadOnlyList<BuiltArtifact> Artifacts,
    bool HasManifestConfig);

/// <summary>
/// Extension point for ecosystem-agnostic artifact building, driver packaging, and hashing.
/// </summary>
public interface IArtifactBuilder
{
    /// <summary>
    /// Builds and packages configured release assets into <see cref="ArtifactBuildRequest.OutputDirectory"/>.
    /// </summary>
    Task<ArtifactBuildResult> BuildAsync(ArtifactBuildRequest request, CancellationToken ct = default);
}
