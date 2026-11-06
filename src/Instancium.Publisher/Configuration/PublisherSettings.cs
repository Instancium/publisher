namespace Instancium.Publisher.Configuration;

using System;
using System.Collections.Generic;

/// <summary>
/// Configuration for controlling how Git diff payloads are optimized before LLM inference.
/// </summary>
public sealed class DiffOptimizationSettings
{
    public bool Enabled { get; set; } = true;
    public int ContextLines { get; set; } = 0;
    public bool IgnoreWhitespace { get; set; } = true;
    public int MaxHunkLines { get; set; } = 80;
}

/// <summary>
/// Strongly typed configuration for Instancium.Publisher execution.
/// </summary>
public sealed class PublisherSettings
{
    public string TargetBranch { get; set; } = "main";
    public string ReleaseBranchPrefix { get; set; } = "release-prep/v";
    public string[] AllowedBranchPrefixes { get; set; } = ["dev", "feat/", "fix/", "hotfix/", "rc/", "rfc/"];

    // AI Configuration (to be used by the new HTTP REST Client in Stage 3)
    public string AiGatewayUrl { get; set; } = "http://localhost:5000";
    public string AiModelId { get; set; } = "qwen3-vl-8b-instruct";
    public string AiApiKey { get; set; } = string.Empty;
    public int MaxChunkLength { get; set; } = 3000;

    public Dictionary<string, string> RegisteredProjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DiffOptimizationSettings DiffOptimization { get; set; } = new();
}