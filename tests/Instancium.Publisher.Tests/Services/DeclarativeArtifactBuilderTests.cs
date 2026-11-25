namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Domain.Versioning;
using Instancium.Publisher.Infrastructure.Publishing;
using Instancium.Publisher.Tests.TestInfrastructure;
using Moq;
using System.IO.Compression;
using Xunit;

/// <summary>
/// Verifies declarative artifact building from instancium.publish.json:
/// graceful no-op when absent, command execution with token expansion, zip packaging,
/// external driver bundling, SHA-256 calculation, and failure propagation.
/// </summary>
public sealed class DeclarativeArtifactBuilderTests
{
    private readonly Mock<IProcessExecutionService> _processMock = new();
    private readonly Mock<IProgressReporter> _reporterMock = new();

    [Fact]
    public async Task BuildAsync_NoPublishManifest_ReturnsNoConfigAndEmptyArtifacts()
    {
        using var scope = new TemporaryDirectoryScope();
        string outputDir = scope.CreateDirectory("out");
        var builder = new DeclarativeArtifactBuilder(_processMock.Object, _reporterMock.Object);

        ArtifactBuildResult result = await builder.BuildAsync(
            new ArtifactBuildRequest(scope.RootPath, "Demo", SemanticVersion.Parse("1.2.0"), outputDir));

        Assert.False(result.HasManifestConfig);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task BuildAsync_TargetCommand_ExpandsTokensPackagesZipAndComputesSha256()
    {
        using var scope = new TemporaryDirectoryScope();
        string outputDir = scope.CreateDirectory("out");
        string configJson = """
        {
          "product": "Instancium.Vectra",
          "targets": [
            {
              "name": "win-x64",
              "command": "dotnet",
              "arguments": ["publish", "-o", "{stageDir}", "-p:Version={version}"],
              "archiveName": "vectra-{version}-win-x64.zip"
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, PublishManifestConfig.FileName), configJson);

        _processMock
            .Setup(p => p.ExecuteAsync(
                "dotnet",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string, Action<string>?, Action<string>?, CancellationToken>(
                (_, args, _, _, _, _) =>
                {
                    int outIndex = args.ToList().IndexOf("-o");
                    string stageDir = args[outIndex + 1];
                    File.WriteAllText(Path.Combine(stageDir, "Vectra.exe"), "binary-payload");
                })
            .ReturnsAsync(0);

        var builder = new DeclarativeArtifactBuilder(_processMock.Object, _reporterMock.Object);
        ArtifactBuildResult result = await builder.BuildAsync(
            new ArtifactBuildRequest(scope.RootPath, "Fallback", SemanticVersion.Parse("2.0.0"), outputDir));

        Assert.True(result.HasManifestConfig);
        Assert.Equal("Instancium.Vectra", result.ProductName);
        Assert.Single(result.Artifacts);
        BuiltArtifact artifact = result.Artifacts[0];
        Assert.Equal("vectra-2.0.0-win-x64.zip", artifact.RelativePath);
        Assert.True(File.Exists(artifact.FullPath));
        Assert.Equal(64, artifact.Sha256.Length);
        Assert.True(artifact.Size > 0);

        using ZipArchive zip = ZipFile.OpenRead(artifact.FullPath);
        Assert.Contains(zip.Entries, e => e.FullName == "Vectra.exe");
    }

    [Fact]
    public async Task BuildAsync_ExternalAssets_PackagesDirectoryAndCopiesFile()
    {
        using var scope = new TemporaryDirectoryScope();
        string outputDir = scope.CreateDirectory("out");
        string driverDir = scope.CreateDirectory("drivers/vulkan");
        await File.WriteAllTextAsync(Path.Combine(driverDir, "vulkan-1.dll"), "vk-bytes");
        string readmeAsset = Path.Combine(scope.RootPath, "drivers", "llama-bin.zip");
        await File.WriteAllTextAsync(readmeAsset, "llama-zip-bytes");

        string configJson = """
        {
          "externalAssets": [
            { "sourcePath": "drivers/vulkan", "archiveName": "vulkan-{version}.zip" },
            { "sourcePath": "drivers/llama-bin.zip" }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, PublishManifestConfig.FileName), configJson);

        var builder = new DeclarativeArtifactBuilder(_processMock.Object, _reporterMock.Object);
        ArtifactBuildResult result = await builder.BuildAsync(
            new ArtifactBuildRequest(scope.RootPath, "Vectra", SemanticVersion.Parse("1.5.0"), outputDir));

        Assert.True(result.HasManifestConfig);
        Assert.Equal(2, result.Artifacts.Count);
        Assert.Contains(result.Artifacts, a => a.RelativePath == "llama-bin.zip");
        Assert.Contains(result.Artifacts, a => a.RelativePath == "vulkan-1.5.0.zip");
    }

    [Fact]
    public async Task BuildAsync_TargetCommandFails_ThrowsInvalidOperationException()
    {
        using var scope = new TemporaryDirectoryScope();
        string outputDir = scope.CreateDirectory("out");
        string configJson = """
        {
          "targets": [
            { "name": "rust-core", "command": "cargo", "arguments": ["build", "--release"] }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(scope.RootPath, PublishManifestConfig.FileName), configJson);

        _processMock
            .Setup(p => p.ExecuteAsync(
                "cargo",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<string>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, string, Action<string>?, Action<string>?, CancellationToken>(
                (_, _, _, _, onErr, _) => onErr?.Invoke("linker error"))
            .ReturnsAsync(1);

        var builder = new DeclarativeArtifactBuilder(_processMock.Object, _reporterMock.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(new ArtifactBuildRequest(scope.RootPath, "Core", SemanticVersion.Parse("1.0.0"), outputDir)));

        Assert.Contains("linker error", ex.Message);
    }
}
