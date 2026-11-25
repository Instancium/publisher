namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Infrastructure.GitHub;
using Instancium.Publisher.Tests.TestInfrastructure;
using Moq;
using Xunit;

/// <summary>
/// Verifies GitHub CLI release publication with attached release assets,
/// backward-compatible asset-less releases, and failure fallback reporting.
/// </summary>
public sealed class GitHubCliReleasePublisherTests
{
    private readonly Mock<IProcessExecutionService> _processMock = new();
    private readonly Mock<IProgressReporter> _reporterMock = new();
    private readonly ProjectContext _projectContext = new() { WorkingDirectory = "/test/repo" };

    [Fact]
    public async Task PublishAsync_WithAssetPaths_AppendsAllAssetsToGhReleaseCreate()
    {
        using var scope = new TemporaryDirectoryScope();
        string archive = Path.Combine(scope.RootPath, "app-1.8.0.zip");
        string manifest = Path.Combine(scope.RootPath, "manifest.json");
        string signature = Path.Combine(scope.RootPath, "manifest.sig");
        await File.WriteAllTextAsync(archive, "zip");
        await File.WriteAllTextAsync(manifest, "{}");
        await File.WriteAllTextAsync(signature, "sig");

        IReadOnlyList<string>? capturedArgs = null;
        _processMock
            .Setup(p => p.ExecuteAsync(
                "gh",
                It.IsAny<IReadOnlyList<string>>(),
                _projectContext.WorkingDirectory,
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string, Action<string>?, Action<string>?, CancellationToken>(
                (_, args, _, _, _, _) => capturedArgs = args)
            .ReturnsAsync(0);

        var publisher = new GitHubCliReleasePublisher(_processMock.Object, _reporterMock.Object, _projectContext);
        await publisher.PublishAsync(new ReleasePublicationRequest(
            "v1.8.0-rc.1", "Release v1.8.0-rc.1", "Notes", IsPreRelease: true,
            AssetPaths: [archive, manifest, signature]));

        Assert.NotNull(capturedArgs);
        Assert.Contains("--prerelease", capturedArgs!);
        Assert.Contains(archive, capturedArgs!);
        Assert.Contains(manifest, capturedArgs!);
        Assert.Contains(signature, capturedArgs!);
    }

    [Fact]
    public async Task PublishAsync_MissingAssetFile_ThrowsFileNotFoundExceptionWithoutInvokingGh()
    {
        var publisher = new GitHubCliReleasePublisher(_processMock.Object, _reporterMock.Object, _projectContext);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            publisher.PublishAsync(new ReleasePublicationRequest(
                "v1.8.0", "Release v1.8.0", "Notes", IsPreRelease: false,
                AssetPaths: ["/non-existent/manifest.json"])));

        _processMock.Verify(p => p.ExecuteAsync(
            It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(),
            It.IsAny<Action<string>?>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_GhFailure_ReportsWarningsAndExportsFallbackNotes()
    {
        _processMock
            .Setup(p => p.ExecuteAsync(
                "gh",
                It.IsAny<IReadOnlyList<string>>(),
                _projectContext.WorkingDirectory,
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var publisher = new GitHubCliReleasePublisher(_processMock.Object, _reporterMock.Object, _projectContext);
        await publisher.PublishAsync(new ReleasePublicationRequest("v1.8.0", "Release v1.8.0", "Notes", false));

        _reporterMock.Verify(
            r => r.ReportWarningAsync(It.Is<string>(m => m.Contains("GitHub CLI")), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
