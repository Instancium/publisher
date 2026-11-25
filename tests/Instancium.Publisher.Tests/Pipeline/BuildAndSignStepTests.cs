namespace Instancium.Publisher.Tests.Pipeline;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.Pipeline;
using Instancium.Publisher.Application.Pipeline.Steps;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Configuration;
using Instancium.Publisher.Infrastructure.Publishing;
using Instancium.Publisher.Infrastructure.Signing;
using Instancium.Publisher.Tests.TestInfrastructure;
using Instancium.Trust.Algorithms;
using Instancium.Trust.Ceremony;
using Instancium.Trust.Certificates;
using Instancium.Trust.Keys;
using Instancium.Trust.Manifests;
using Instancium.Trust.Verification;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// Verifies end-to-end build, signing, pipeline journaling, and cryptographic verification
/// of the emitted manifest and artifacts through Instancium.Trust.
/// </summary>
public sealed class BuildAndSignStepTests
{
    private readonly Mock<IProgressReporter> _reporterMock = new();

    [Fact]
    public async Task ExecuteAsync_WithDeclarativeConfig_BuildsSignsJournalsAndPassesTrustVerification()
    {
        using var scope = new TemporaryDirectoryScope();
        string ceremonyDir = scope.CreateDirectory("ceremony");
        string repoDir = scope.CreateDirectory("repo");
        string driverDir = scope.CreateDirectory("repo/drivers/vulkan");
        await File.WriteAllTextAsync(Path.Combine(driverDir, "vk.dll"), "driver-payload");
        await File.WriteAllTextAsync(Path.Combine(repoDir, "app.bin"), "app-payload");

        string configJson = """
        {
          "product": "Instancium.Vectra",
          "externalAssets": [
            { "sourcePath": "drivers/vulkan", "archiveName": "vulkan-{version}.zip" },
            { "sourcePath": "app.bin" }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(repoDir, PublishManifestConfig.FileName), configJson);

        var registry = new SignatureAlgorithmRegistry([new Ed25519SignatureAlgorithm()]);
        var codec = new KeyContainerCodec(registry, new KeyContainerProtector());
        var ceremony = new KeyCeremonyService(registry, codec);
        KeyCeremonyResult ceremonyResult = ceremony.Execute(new KeyCeremonyOptions
        {
            OutputDirectory = ceremonyDir,
            RootContainerPassword = "root-secret-password",
            ReleaseContainerPassword = "release-secret-password",
            Container = new KeyContainerOptions { MemoryKiB = 64, Iterations = 1, Parallelism = 1 }
        });

        var secretMock = new Mock<ISecretProvider>();
        secretMock.Setup(s => s.GetReleaseKeyPasswordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("release-secret-password");

        var settings = Options.Create(new PublisherSettings
        {
            Signing = new SigningSettings
            {
                ReleaseKeyContainerPath = Path.Combine(ceremonyDir, "release-key.key.json"),
                ReleaseCertificatePath = Path.Combine(ceremonyDir, "release-key.cert")
            }
        });

        var builder = new DeclarativeArtifactBuilder(new Mock<IProcessExecutionService>().Object, _reporterMock.Object);
        var signer = new LocalReleaseKeySigner(codec, new ManifestSigner(registry), secretMock.Object, settings);
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BuildAndSignStep(builder, signer, stateManager, _reporterMock.Object);

        var context = new PipelineContext
        {
            CurrentStep = 3,
            NewVersion = SemanticVersion.Parse("1.8.0")
        };

        IReadOnlyList<string> assets = await step.ExecuteAsync(context, repoDir, "Instancium.Vectra");

        Assert.Equal(4, assets.Count);
        Assert.Equal(1, stateManager.SaveCount);
        Assert.Equal(4, stateManager.SavedContexts[0].ReleaseAssetPaths.Count);

        string manifestPath = assets.Single(p => Path.GetFileName(p) == LocalReleaseKeySigner.ManifestFileName);
        string signaturePath = assets.Single(p => Path.GetFileName(p) == LocalReleaseKeySigner.SignatureFileName);

        byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        byte[] signatureBytes = DetachedSignature.FromText(await File.ReadAllTextAsync(signaturePath));
        Assert.True(SignedEnvelope.TryDecode(ceremonyResult.ReleaseCertificateEnvelope, out SignedEnvelope? certEnvelope));

        var verifier = new TrustVerificationEngine(registry);
        VerificationResult chainResult = verifier.VerifyManifest(
            manifestBytes,
            signatureBytes,
            certEnvelope!,
            [ceremonyResult.PrimaryRoot.ToAnchor()]);

        Assert.True(chainResult.IsValid, chainResult.FailureReason);

        ReleaseManifest manifest = ReleaseManifest.FromJsonBytes(manifestBytes);
        Assert.Equal(2, manifest.Artifacts.Count);
        foreach (ManifestArtifact entry in manifest.Artifacts)
        {
            string filePath = assets.Single(p => Path.GetFileName(p) == entry.Path);
            VerificationResult fileResult = verifier.VerifyArtifact(await File.ReadAllBytesAsync(filePath), entry);
            Assert.True(fileResult.IsValid, fileResult.FailureReason);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithoutDeclarativeConfig_SkipsSigningAndJournalsEmptyAssets()
    {
        using var scope = new TemporaryDirectoryScope();
        var builder = new DeclarativeArtifactBuilder(new Mock<IProcessExecutionService>().Object, _reporterMock.Object);
        var signerMock = new Mock<IArtifactSigner>();
        var stateManager = new InMemoryPipelineStateManager();
        var step = new BuildAndSignStep(builder, signerMock.Object, stateManager, _reporterMock.Object);

        var context = new PipelineContext
        {
            CurrentStep = 3,
            NewVersion = SemanticVersion.Parse("1.8.0")
        };

        IReadOnlyList<string> assets = await step.ExecuteAsync(context, scope.RootPath, "SimpleLib");

        Assert.Empty(assets);
        Assert.Equal(1, stateManager.SaveCount);
        signerMock.Verify(s => s.SignAsync(It.IsAny<ArtifactSignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NullNewVersion_ThrowsInvalidOperationException()
    {
        var step = new BuildAndSignStep(
            new Mock<IArtifactBuilder>().Object,
            new Mock<IArtifactSigner>().Object,
            new InMemoryPipelineStateManager(),
            _reporterMock.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            step.ExecuteAsync(new PipelineContext(), "/repo", "App"));
    }
}
