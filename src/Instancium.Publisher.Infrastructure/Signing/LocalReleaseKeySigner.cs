namespace Instancium.Publisher.Infrastructure.Signing;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Trust.Certificates;
using Instancium.Trust.Keys;
using Instancium.Trust.Manifests;
using Microsoft.Extensions.Options;
using System.Text;

/// <summary>
/// Unlocks the local Argon2id + AES-256-GCM Release Key container via <c>Instancium.Trust</c>,
/// emits <c>manifest.json</c> and detached <c>manifest.sig</c>, and wipes private key material in <c>finally</c>.
/// </summary>
public sealed class LocalReleaseKeySigner : IArtifactSigner
{
    public const string ManifestFileName = "manifest.json";
    public const string SignatureFileName = "manifest.sig";

    private readonly KeyContainerCodec _containerCodec;
    private readonly ManifestSigner _manifestSigner;
    private readonly ISecretProvider _secretProvider;
    private readonly SigningSettings _settings;
    private readonly TimeProvider _timeProvider;

    public LocalReleaseKeySigner(
        KeyContainerCodec containerCodec,
        ManifestSigner manifestSigner,
        ISecretProvider secretProvider,
        IOptions<PublisherSettings> settings,
        TimeProvider? timeProvider = null)
    {
        _containerCodec = containerCodec ?? throw new ArgumentNullException(nameof(containerCodec));
        _manifestSigner = manifestSigner ?? throw new ArgumentNullException(nameof(manifestSigner));
        _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
        _settings = settings?.Value.Signing ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SignedManifestResult> SignAsync(ArtifactSignRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string manifestFileName = ValidateOutputFileName(request.ManifestFileName, nameof(request.ManifestFileName));
        string signatureFileName = ValidateOutputFileName(request.SignatureFileName, nameof(request.SignatureFileName));

        if (string.IsNullOrWhiteSpace(_settings.ReleaseKeyContainerPath))
        {
            throw new InvalidOperationException(
                "Release key container path is not configured. Set PublisherSettings:Signing:ReleaseKeyContainerPath.");
        }

        if (!File.Exists(_settings.ReleaseKeyContainerPath))
        {
            throw new FileNotFoundException(
                $"Release key container not found: '{_settings.ReleaseKeyContainerPath}'.",
                _settings.ReleaseKeyContainerPath);
        }

        string keyId = await ResolveReleaseKeyIdAsync(ct);
        string containerJson = await File.ReadAllTextAsync(_settings.ReleaseKeyContainerPath, ct);
        string password = await _secretProvider.GetReleaseKeyPasswordAsync(ct);

        TrustKeyPair releaseKey = _containerCodec.Unprotect(containerJson, password);
        try
        {
            List<ManifestArtifact> manifestArtifacts = request.Artifacts
                .Select(a => new ManifestArtifact(a.RelativePath, a.Sha256, a.Size))
                .ToList();

            var manifest = new ReleaseManifest(
                ReleaseManifest.CurrentVersion,
                releaseKey.Alg,
                keyId,
                request.ProductName,
                request.Version.ToString(),
                _timeProvider.GetUtcNow(),
                manifestArtifacts);

            byte[] manifestBytes = manifest.ToJsonBytes();
            string signatureText = _manifestSigner.Sign(manifestBytes, releaseKey);

            Directory.CreateDirectory(request.OutputDirectory);
            string manifestPath = Path.Combine(request.OutputDirectory, manifestFileName);
            string signaturePath = Path.Combine(request.OutputDirectory, signatureFileName);

            await File.WriteAllBytesAsync(manifestPath, manifestBytes, ct);
            await File.WriteAllTextAsync(signaturePath, signatureText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);

            return new SignedManifestResult(manifestPath, signaturePath);
        }
        finally
        {
            releaseKey.WipePrivateKey();
        }
    }

    private static string ValidateOutputFileName(string fileName, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName, paramName);
        string trimmed = fileName.Trim();
        if (!string.Equals(trimmed, Path.GetFileName(trimmed), StringComparison.Ordinal) ||
            trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Output file name '{fileName}' must not contain path segments.", paramName);
        }

        return trimmed;
    }

    private async Task<string> ResolveReleaseKeyIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_settings.ReleaseCertificatePath))
        {
            if (!File.Exists(_settings.ReleaseCertificatePath))
            {
                throw new FileNotFoundException(
                    $"Release certificate not found: '{_settings.ReleaseCertificatePath}'.",
                    _settings.ReleaseCertificatePath);
            }

            string certText = await File.ReadAllTextAsync(_settings.ReleaseCertificatePath, ct);
            if (!SignedEnvelope.TryDecode(certText, out SignedEnvelope? envelope) || envelope is null)
            {
                throw new InvalidOperationException(
                    $"Release certificate '{_settings.ReleaseCertificatePath}' is not a valid signed envelope.");
            }

            ReleaseKeyCertificate cert = ReleaseKeyCertificate.FromJsonBytes(envelope.Payload);
            if (_timeProvider.GetUtcNow() > cert.ExpiresAt)
            {
                throw new InvalidOperationException(
                    $"Release key certificate '{cert.Kid}' expired at {cert.ExpiresAt:O}. Rotate the release key before publishing.");
            }

            return cert.Kid;
        }

        if (!string.IsNullOrWhiteSpace(_settings.ReleaseKeyId))
        {
            return _settings.ReleaseKeyId.Trim();
        }

        throw new InvalidOperationException(
            "Release key identifier cannot be resolved. Configure PublisherSettings:Signing:ReleaseCertificatePath or ReleaseKeyId.");
    }
}
