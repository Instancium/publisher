namespace Instancium.Publisher.Infrastructure.Publishing;

using Instancium.Publisher.Application.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Reads <c>instancium.publish.json</c> from the target repository, executes configured ecosystem build
/// commands, packages outputs and external driver bundles into zip archives, and computes streaming SHA-256 hashes.
/// </summary>
public sealed class DeclarativeArtifactBuilder : IArtifactBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IProcessExecutionService _processService;
    private readonly IProgressReporter _progressReporter;

    public DeclarativeArtifactBuilder(
        IProcessExecutionService processService,
        IProgressReporter progressReporter)
    {
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
    }

    public async Task<ArtifactBuildResult> BuildAsync(ArtifactBuildRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);

        string configPath = Path.Combine(request.RepositoryRoot, PublishManifestConfig.FileName);
        if (!File.Exists(configPath))
        {
            return new ArtifactBuildResult(request.ProductName, [], HasManifestConfig: false);
        }

        PublishManifestConfig config = await LoadConfigAsync(configPath, ct);
        string productName = string.IsNullOrWhiteSpace(config.Product) ? request.ProductName : config.Product.Trim();
        string versionText = request.Version.ToString();

        PrepareCleanDirectory(request.OutputDirectory);
        string workRoot = Path.Combine(request.OutputDirectory, ".work");
        Directory.CreateDirectory(workRoot);

        var producedFiles = new List<string>();
        try
        {
            for (int i = 0; i < config.Targets.Count; i++)
            {
                PublishBuildTarget target = config.Targets[i];
                string file = await ExecuteTargetAsync(target, i, request, workRoot, versionText, ct);
                producedFiles.Add(file);
            }

            foreach (PublishExternalAsset asset in config.ExternalAssets)
            {
                string file = PackageExternalAsset(asset, request, versionText);
                producedFiles.Add(file);
            }
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }

        var artifacts = new List<BuiltArtifact>(producedFiles.Count);
        foreach (string fullPath in producedFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            artifacts.Add(await CreateArtifactDescriptorAsync(fullPath, request.OutputDirectory, ct));
        }

        return new ArtifactBuildResult(productName, artifacts, HasManifestConfig: true);
    }

    private static async Task<PublishManifestConfig> LoadConfigAsync(string configPath, CancellationToken ct)
    {
        string json = await File.ReadAllTextAsync(configPath, ct);
        PublishManifestConfig? config = JsonSerializer.Deserialize<PublishManifestConfig>(json, JsonOptions);
        if (config is null)
        {
            throw new InvalidOperationException($"Publish configuration '{configPath}' deserialized to null.");
        }

        return config;
    }

    private async Task<string> ExecuteTargetAsync(
        PublishBuildTarget target,
        int index,
        ArtifactBuildRequest request,
        string workRoot,
        string versionText,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.Command))
        {
            throw new InvalidOperationException($"Build target '{target.Name}' does not specify a command.");
        }

        string targetName = string.IsNullOrWhiteSpace(target.Name) ? $"target-{index + 1}" : target.Name.Trim();
        string stageDir = Path.Combine(workRoot, SanitizeFileSegment(targetName));
        Directory.CreateDirectory(stageDir);

        string workingDirectory = Path.IsPathRooted(target.WorkingDirectory)
            ? target.WorkingDirectory
            : Path.GetFullPath(Path.Combine(request.RepositoryRoot, target.WorkingDirectory));

        List<string> resolvedArgs = target.Arguments
            .Select(arg => ExpandTokens(arg, versionText, stageDir, request.OutputDirectory, request.RepositoryRoot))
            .ToList();

        await _progressReporter.ReportAsync($"Building target '{targetName}' ({target.Command})...", ct);

        var outputBuilder = new StringBuilder();
        int exitCode = await _processService.ExecuteAsync(
            target.Command,
            resolvedArgs,
            workingDirectory,
            msg => outputBuilder.AppendLine(msg),
            msg => outputBuilder.AppendLine(msg),
            ct);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Build target '{targetName}' failed (exit code {exitCode}). Output:\n{outputBuilder}");
        }

        if (!string.IsNullOrWhiteSpace(target.OutputFile))
        {
            string expandedSource = ExpandTokens(target.OutputFile, versionText, stageDir, request.OutputDirectory, request.RepositoryRoot);
            string sourcePath = Path.IsPathRooted(expandedSource)
                ? expandedSource
                : Path.GetFullPath(Path.Combine(workingDirectory, expandedSource));

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Build target '{targetName}' did not produce expected file '{sourcePath}'.", sourcePath);
            }

            string destinationName = string.IsNullOrWhiteSpace(target.ArchiveName)
                ? Path.GetFileName(sourcePath)
                : ExpandTokens(target.ArchiveName, versionText, stageDir, request.OutputDirectory, request.RepositoryRoot);

            string destinationPath = ResolveSafeOutputPath(request.OutputDirectory, destinationName);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return destinationPath;
        }

        string archiveName = string.IsNullOrWhiteSpace(target.ArchiveName)
            ? $"{SanitizeFileSegment(targetName)}-{versionText}.zip"
            : ExpandTokens(target.ArchiveName, versionText, stageDir, request.OutputDirectory, request.RepositoryRoot);

        string archivePath = ResolveSafeOutputPath(request.OutputDirectory, archiveName);
        ZipFile.CreateFromDirectory(stageDir, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return archivePath;
    }

    private static string PackageExternalAsset(
        PublishExternalAsset asset,
        ArtifactBuildRequest request,
        string versionText)
    {
        if (string.IsNullOrWhiteSpace(asset.SourcePath))
        {
            throw new InvalidOperationException("External asset entry must specify a non-empty SourcePath.");
        }

        string expandedSource = ExpandTokens(
            asset.SourcePath, versionText, request.OutputDirectory, request.OutputDirectory, request.RepositoryRoot);

        string sourcePath = Path.IsPathRooted(expandedSource)
            ? expandedSource
            : Path.GetFullPath(Path.Combine(request.RepositoryRoot, expandedSource));

        if (Directory.Exists(sourcePath))
        {
            string defaultName = $"{Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}-{versionText}.zip";
            string archiveName = string.IsNullOrWhiteSpace(asset.ArchiveName)
                ? defaultName
                : ExpandTokens(asset.ArchiveName, versionText, request.OutputDirectory, request.OutputDirectory, request.RepositoryRoot);

            string archivePath = ResolveSafeOutputPath(request.OutputDirectory, archiveName);
            ZipFile.CreateFromDirectory(sourcePath, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return archivePath;
        }

        if (File.Exists(sourcePath))
        {
            string destinationName = string.IsNullOrWhiteSpace(asset.ArchiveName)
                ? Path.GetFileName(sourcePath)
                : ExpandTokens(asset.ArchiveName, versionText, request.OutputDirectory, request.OutputDirectory, request.RepositoryRoot);

            string destinationPath = ResolveSafeOutputPath(request.OutputDirectory, destinationName);
            File.Copy(sourcePath, destinationPath, overwrite: true);
            return destinationPath;
        }

        throw new FileNotFoundException($"External release asset not found: '{sourcePath}'.", sourcePath);
    }

    private static async Task<BuiltArtifact> CreateArtifactDescriptorAsync(
        string fullPath,
        string outputDirectory,
        CancellationToken ct)
    {
        var fileInfo = new FileInfo(fullPath);
        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

        byte[] hashBytes = await SHA256.HashDataAsync(stream, ct);
        string sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        string relativePath = Path.GetRelativePath(outputDirectory, fullPath).Replace('\\', '/');

        return new BuiltArtifact(relativePath, fullPath, sha256, fileInfo.Length);
    }

    private static string ExpandTokens(
        string template,
        string version,
        string stageDir,
        string outputDir,
        string repoRoot)
    {
        return template
            .Replace("{version}", version, StringComparison.OrdinalIgnoreCase)
            .Replace("{stageDir}", stageDir, StringComparison.OrdinalIgnoreCase)
            .Replace("{outputDir}", outputDir, StringComparison.OrdinalIgnoreCase)
            .Replace("{repoRoot}", repoRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSafeOutputPath(string outputDirectory, string fileName)
    {
        string normalizedRoot = Path.GetFullPath(outputDirectory);
        string candidate = Path.GetFullPath(Path.Combine(normalizedRoot, fileName));
        if (!candidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Artifact output name '{fileName}' escapes the staging directory.");
        }

        return candidate;
    }

    private static string SanitizeFileSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        return sb.ToString();
    }

    private static void PrepareCleanDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
