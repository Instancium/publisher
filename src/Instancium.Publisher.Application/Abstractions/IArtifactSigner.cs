namespace Instancium.Publisher.Application.Abstractions;

using Instancium.Publisher.Domain.Versioning;

/// <summary>
/// Input parameters for generating and cryptographically signing a release manifest.
/// </summary>
public sealed record ArtifactSignRequest(
    string ProductName,
    SemanticVersion Version,
    IReadOnlyList<BuiltArtifact> Artifacts,
    string OutputDirectory);

/// <summary>
/// Paths to the emitted release manifest and its detached signature file.
/// </summary>
public sealed record SignedManifestResult(string ManifestPath, string SignaturePath);

/// <summary>
/// Extension point for cryptographic release signing (Ed25519/PQC via Instancium.Trust, future HSM/Authenticode).
/// </summary>
public interface IArtifactSigner
{
    /// <summary>
    /// Produces a signed manifest and detached signature covering all built artifacts.
    /// </summary>
    Task<SignedManifestResult> SignAsync(ArtifactSignRequest request, CancellationToken ct = default);
}
