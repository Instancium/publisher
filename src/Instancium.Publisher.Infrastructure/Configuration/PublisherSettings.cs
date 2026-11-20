namespace Instancium.Publisher.Infrastructure.Configuration;

/// <summary>
/// Strongly typed configuration for Instancium.Publisher execution.
/// </summary>
public sealed class PublisherSettings
{
    public string TargetBranch { get; set; } = "main";
    public string ReleaseBranchPrefix { get; set; } = "release-prep/v";

    // AI Configuration (to be used by the new HTTP REST Client in Stage 3)
    public string AiGatewayUrl { get; set; } = "http://localhost:5000";
    public string AiModelId { get; set; } = "qwen3-vl-8b-instruct";
    public string AiApiKey { get; set; } = string.Empty;
    public int MaxChunkLength { get; set; } = 3000;

    public DiffOptimizationSettings DiffOptimization { get; set; } = new();
}