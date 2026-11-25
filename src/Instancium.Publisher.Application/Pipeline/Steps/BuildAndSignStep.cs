namespace Instancium.Publisher.Application.Pipeline.Steps;

using Instancium.Publisher.Application.Abstractions;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Builds declarative release artifacts into a repository-isolated temp staging directory,
/// signs the resulting manifest, and journals asset paths into pipeline state before publication.
/// Keeping staging outside the working tree prevents accidental Git commits of release binaries.
/// </summary>
public sealed class BuildAndSignStep
{
    private readonly IArtifactBuilder _artifactBuilder;
    private readonly IArtifactSigner _artifactSigner;
    private readonly IPipelineStateManager _stateManager;
    private readonly IProgressReporter _progressReporter;

    public BuildAndSignStep(
        IArtifactBuilder artifactBuilder,
        IArtifactSigner artifactSigner,
        IPipelineStateManager stateManager,
        IProgressReporter progressReporter)
    {
        _artifactBuilder = artifactBuilder ?? throw new ArgumentNullException(nameof(artifactBuilder));
        _artifactSigner = artifactSigner ?? throw new ArgumentNullException(nameof(artifactSigner));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
    }

    /// <summary>
    /// Executes build and signing for the planned release version and persists asset paths to the pipeline journal.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExecuteAsync(
        PipelineContext context,
        string repositoryRoot,
        string productName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);

        if (context.NewVersion is null)
        {
            throw new InvalidOperationException("New version is not set in the pipeline context.");
        }

        string stagingDirectory = ResolveStagingDirectory(repositoryRoot, context.NewVersion.ToString());
        await _progressReporter.ReportAsync("Building declarative release artifacts...", ct);

        ArtifactBuildResult buildResult = await _artifactBuilder.BuildAsync(
            new ArtifactBuildRequest(repositoryRoot, productName, context.NewVersion, stagingDirectory),
            ct);

        if (!buildResult.HasManifestConfig || buildResult.Artifacts.Count == 0)
        {
            context.ReleaseAssetPaths = [];
            await _stateManager.SaveAsync(context);
            return context.ReleaseAssetPaths;
        }

        await _progressReporter.ReportAsync(
            $"Signing manifest for {buildResult.Artifacts.Count} artifact(s)...",
            ct);

        SignedManifestResult signed = await _artifactSigner.SignAsync(
            new ArtifactSignRequest(
                buildResult.ProductName,
                context.NewVersion,
                buildResult.Artifacts,
                stagingDirectory),
            ct);

        var assetPaths = new List<string>(buildResult.Artifacts.Count + 2);
        assetPaths.AddRange(buildResult.Artifacts.Select(a => a.FullPath));
        assetPaths.Add(signed.ManifestPath);
        assetPaths.Add(signed.SignaturePath);

        context.ReleaseAssetPaths = assetPaths;
        await _stateManager.SaveAsync(context);
        return assetPaths;
    }

    private static string ResolveStagingDirectory(string repositoryRoot, string version)
    {
        string normalizedRoot = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
        string shortHash = Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "Instancium.Publisher", "staging", shortHash, $"v{version}");
    }
}
