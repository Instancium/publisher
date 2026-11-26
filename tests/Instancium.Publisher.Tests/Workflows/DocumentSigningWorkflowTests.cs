namespace Instancium.Publisher.Tests.Workflows;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Application.Workflows;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Configuration;
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
/// Verifies standalone document signing end-to-end with Instancium.Trust chain verification
/// and isolated Git publication context handling.
/// </summary>
public sealed class DocumentSigningWorkflowTests
{
    [Fact]
    public async Task SignDocumentAsync_CopiesDocumentSignsManifestAndPassesTrustVerification()
    {
        using var scope = new TemporaryDirectoryScope();
        string ceremonyDir = scope.CreateDirectory("ceremony");
        string sourceDir = scope.CreateDirectory("drafts");
        string outputDir = scope.CreateDirectory("hub/doctrine");

        string sourcePdf = Path.Combine(sourceDir, "Instancium_Doctrine.pdf");
        byte[] pdfBytes = "%PDF-1.7\nInstancium Doctrine Payload\n%%EOF"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePdf, pdfBytes);

        var registry = new SignatureAlgorithmRegistry([new Ed25519SignatureAlgorithm()]);
        var codec = new KeyContainerCodec(registry, new KeyContainerProtector());
        var ceremony = new KeyCeremonyService(registry, codec);
        KeyCeremonyResult ceremonyResult = ceremony.Execute(new KeyCeremonyOptions
        {
            OutputDirectory = ceremonyDir,
            RootContainerPassword = "root-password",
            ReleaseContainerPassword = "release-password",
            Container = new KeyContainerOptions { MemoryKiB = 64, Iterations = 1, Parallelism = 1 }
        });

        var secretMock = new Mock<ISecretProvider>();
        secretMock.Setup(s => s.GetReleaseKeyPasswordAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("release-password");

        var settings = Options.Create(new PublisherSettings
        {
            Signing = new SigningSettings
            {
                ReleaseKeyContainerPath = Path.Combine(ceremonyDir, "release-key.key.json"),
                ReleaseCertificatePath = Path.Combine(ceremonyDir, "release-key.cert")
            }
        });

        var signer = new LocalReleaseKeySigner(codec, new ManifestSigner(registry), secretMock.Object, settings);
        var workflow = new DocumentSigningWorkflow(signer, new Mock<IGitService>().Object, new ProjectContext());

        DocumentSigningResult result = await workflow.SignDocumentAsync(new DocumentSigningRequest(
            sourcePdf,
            outputDir,
            "Instancium.Doctrine",
            SemanticVersion.Parse("1.0.0")));

        Assert.Equal(Path.Combine(outputDir, "Instancium_Doctrine.pdf"), result.TargetFilePath);
        Assert.Equal(Path.Combine(outputDir, "doctrine.manifest.json"), result.ManifestPath);
        Assert.Equal(Path.Combine(outputDir, "doctrine.sig"), result.SignaturePath);
        Assert.Equal(pdfBytes.Length, result.Size);
        Assert.Equal(64, result.Sha256.Length);

        byte[] manifestBytes = await File.ReadAllBytesAsync(result.ManifestPath);
        byte[] signatureBytes = DetachedSignature.FromText(await File.ReadAllTextAsync(result.SignaturePath));
        Assert.True(SignedEnvelope.TryDecode(ceremonyResult.ReleaseCertificateEnvelope, out SignedEnvelope? certEnvelope));

        var verifier = new TrustVerificationEngine(registry);
        VerificationResult chain = verifier.VerifyManifest(
            manifestBytes,
            signatureBytes,
            certEnvelope!,
            [ceremonyResult.PrimaryRoot.ToAnchor()]);

        Assert.True(chain.IsValid, chain.FailureReason);

        ReleaseManifest manifest = ReleaseManifest.FromJsonBytes(manifestBytes);
        Assert.Equal("Instancium.Doctrine", manifest.Product);
        Assert.Single(manifest.Artifacts);
        VerificationResult fileCheck = verifier.VerifyArtifact(
            await File.ReadAllBytesAsync(result.TargetFilePath),
            manifest.Artifacts[0]);
        Assert.True(fileCheck.IsValid, fileCheck.FailureReason);
    }

    [Fact]
    public async Task SignDocumentAsync_SameSourceAndOutputDirectory_SignsInPlace()
    {
        using var scope = new TemporaryDirectoryScope();
        string docPath = Path.Combine(scope.RootPath, "Instancium_Doctrine.pdf");
        await File.WriteAllTextAsync(docPath, "pdf-content");

        var signerMock = new Mock<IArtifactSigner>();
        signerMock
            .Setup(s => s.SignAsync(It.IsAny<ArtifactSignRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignedManifestResult(
                Path.Combine(scope.RootPath, "doctrine.manifest.json"),
                Path.Combine(scope.RootPath, "doctrine.sig")));

        var workflow = new DocumentSigningWorkflow(signerMock.Object, new Mock<IGitService>().Object, new ProjectContext());
        DocumentSigningResult result = await workflow.SignDocumentAsync(new DocumentSigningRequest(
            docPath, scope.RootPath, "Instancium.Doctrine", SemanticVersion.Parse("1.0.0")));

        Assert.Equal(docPath, result.TargetFilePath);
        signerMock.Verify(s => s.SignAsync(
            It.Is<ArtifactSignRequest>(r =>
                r.ManifestFileName == "doctrine.manifest.json" &&
                r.SignatureFileName == "doctrine.sig" &&
                r.Artifacts.Count == 1 &&
                r.Artifacts[0].RelativePath == "Instancium_Doctrine.pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SignDocumentAsync_MissingSourceFile_ThrowsFileNotFoundException()
    {
        using var scope = new TemporaryDirectoryScope();
        var workflow = new DocumentSigningWorkflow(
            new Mock<IArtifactSigner>().Object,
            new Mock<IGitService>().Object,
            new ProjectContext());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            workflow.SignDocumentAsync(new DocumentSigningRequest(
                Path.Combine(scope.RootPath, "missing.pdf"),
                scope.RootPath,
                "Instancium.Doctrine",
                SemanticVersion.Parse("1.0.0"))));
    }

    [Fact]
    public async Task PublishToGitAsync_CommitsPushesAndRestoresOriginalProjectContext()
    {
        using var scope = new TemporaryDirectoryScope();
        string originalDir = scope.CreateDirectory("publisher-repo");
        string hubDir = scope.CreateDirectory("instancium-hub");

        var projectContext = new ProjectContext { WorkingDirectory = originalDir };
        var gitMock = new Mock<IGitService>();
        gitMock.Setup(g => g.HasUncommittedChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        gitMock.Setup(g => g.GetCurrentBranchAsync(It.IsAny<CancellationToken>())).ReturnsAsync("main");

        string? directoryDuringCommit = null;
        gitMock.Setup(g => g.CommitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => directoryDuringCommit = projectContext.WorkingDirectory)
            .Returns(Task.CompletedTask);

        var workflow = new DocumentSigningWorkflow(new Mock<IArtifactSigner>().Object, gitMock.Object, projectContext);

        string branch = await workflow.PublishToGitAsync(hubDir, "chore(doctrine): sign v1.0.0");

        Assert.Equal("main", branch);
        Assert.Equal(hubDir, directoryDuringCommit);
        Assert.Equal(originalDir, projectContext.WorkingDirectory);
        gitMock.Verify(g => g.AddAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        gitMock.Verify(g => g.PushBranchAsync("main", false, It.IsAny<CancellationToken>()), Times.Once);
    }
}
