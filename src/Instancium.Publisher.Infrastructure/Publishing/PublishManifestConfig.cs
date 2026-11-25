namespace Instancium.Publisher.Infrastructure.Publishing;

/// <summary>
/// Declarative repository-level build and packaging specification loaded from <c>instancium.publish.json</c>.
/// Keeps ecosystem build commands (.NET, Node, Rust, Python) and driver packaging data-driven.
/// </summary>
public sealed class PublishManifestConfig
{
    public const string FileName = "instancium.publish.json";

    public string? Product { get; set; }
    public List<PublishBuildTarget> Targets { get; set; } = [];
    public List<PublishExternalAsset> ExternalAssets { get; set; } = [];
}

/// <summary>
/// Single build command target whose staged output is archived or emitted as a release asset.
/// </summary>
public sealed class PublishBuildTarget
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = ".";
    public string? ArchiveName { get; set; }
    public string? OutputFile { get; set; }
}

/// <summary>
/// Pre-built asset or directory (e.g., Vulkan or llama.cpp runtime drivers) included in the release bundle.
/// </summary>
public sealed class PublishExternalAsset
{
    public string SourcePath { get; set; } = string.Empty;
    public string? ArchiveName { get; set; }
}
