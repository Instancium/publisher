namespace Instancium.Publisher.Application.Workflows;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Application.State;
using Instancium.Publisher.Domain.Versioning;
using System.Security.Cryptography;

/// <summary>
/// Parameters for signing a standalone immutable document (such as the Instancium Doctrine PDF).
/// </summary>
public sealed record DocumentSigningRequest(
    string SourceFilePath,
    string OutputDirectory,
    string ProductName,
    SemanticVersion Version,
    string ManifestFileName = "doctrine.manifest.json",
    string SignatureFileName = "doctrine.sig");

/// <summary>
/// Resulting paths and integrity metadata produced by a document signing operation.
/// </summary>
public sealed record DocumentSigningResult(
    string TargetFilePath,
    string ManifestPath,
    string SignaturePath,
    string Sha256,
    long Size);

/// <summary>
/// Orchestrates standalone document signing: stages the immutable file without mutating its bytes,
/// computes streaming SHA-256, delegates manifest signing to <see cref="IArtifactSigner"/>,
/// and optionally commits and pushes the signed bundle to a target Git repository.
/// </summary>
public sealed class DocumentSigningWorkflow
{
    private readonly IArtifactSigner _artifactSigner;
    private readonly IGitService _gitService;
    private readonly ProjectContext _projectContext;

    public DocumentSigningWorkflow(
        IArtifactSigner artifactSigner,
        IGitService gitService,
        ProjectContext projectContext)
    {
        _artifactSigner = artifactSigner ?? throw new ArgumentNullException(nameof(artifactSigner));
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _projectContext = projectContext ?? throw new ArgumentNullException(nameof(projectContext));
    }

    /// <summary>
    /// Copies the source document into <see cref="DocumentSigningRequest.OutputDirectory"/> (if different),
    /// calculates its SHA-256 digest, and emits the signed manifest and detached signature.
    /// </summary>
    public async Task<DocumentSigningResult> SignDocumentAsync(
        DocumentSigningRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProductName);
        ArgumentNullException.ThrowIfNull(request.Version);

        string sourceFullPath = Path.GetFullPath(request.SourceFilePath);
        if (!File.Exists(sourceFullPath))
        {
            throw new FileNotFoundException($"Document file not found: '{sourceFullPath}'.", sourceFullPath);
        }

        string outputFullDir = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputFullDir);

        string fileName = Path.GetFileName(sourceFullPath);
        string targetFullPath = Path.GetFullPath(Path.Combine(outputFullDir, fileName));

        if (!string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFullPath, targetFullPath, overwrite: true);
        }

        var fileInfo = new FileInfo(targetFullPath);
        string sha256;
        await using (var stream = new FileStream(
            targetFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true))
        {
            byte[] hashBytes = await SHA256.HashDataAsync(stream, ct);
            sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        var artifact = new BuiltArtifact(fileName, targetFullPath, sha256, fileInfo.Length);
        SignedManifestResult signed = await _artifactSigner.SignAsync(
            new ArtifactSignRequest(
                request.ProductName.Trim(),
                request.Version,
                [artifact],
                outputFullDir,
                request.ManifestFileName,
                request.SignatureFileName),
            ct);

        return new DocumentSigningResult(
            targetFullPath,
            signed.ManifestPath,
            signed.SignaturePath,
            sha256,
            fileInfo.Length);
    }

    /// <summary>
    /// Stages, commits, and pushes signed document artifacts in the specified Git repository
    /// without permanently altering the active <see cref="ProjectContext"/>.
    /// </summary>
    public async Task<string> PublishToGitAsync(
        string repositoryDirectory,
        string commitMessage,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitMessage);

        string targetRepo = Path.GetFullPath(repositoryDirectory);
        if (!Directory.Exists(targetRepo))
        {
            throw new DirectoryNotFoundException($"Git repository directory not found: '{targetRepo}'.");
        }

        string previousDirectory = _projectContext.WorkingDirectory;
        _projectContext.WorkingDirectory = targetRepo;
        try
        {
            await _gitService.AddAllAsync(ct);
            if (!await _gitService.HasUncommittedChangesAsync(ct))
            {
                throw new InvalidOperationException("No new or modified files detected in the target Git repository.");
            }

            await _gitService.CommitAsync(commitMessage, ct);
            string branch = await _gitService.GetCurrentBranchAsync(ct);
            await _gitService.PushBranchAsync(branch, force: false, ct);
            return branch;
        }
        finally
        {
            _projectContext.WorkingDirectory = previousDirectory;
        }
    }
}
