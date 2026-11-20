namespace Instancium.Publisher.Infrastructure.Configuration;

using System.Collections.Generic;

/// <summary>
/// Controls Git diff extraction and payload reduction parameters prior to LLM inference.
/// </summary>
public sealed class DiffOptimizationSettings
{
    public bool Enabled { get; set; } = true;
    public int ContextLines { get; set; } = 3;
    public bool IgnoreWhitespace { get; set; } = true;
    public int MaxHunkLines { get; set; } = 80;

    /// <summary>
    /// Git pathspec exclusion globs applied at extraction time to prevent lockfiles,
    /// generated sources, and binaries from entering the inference pipeline.
    /// </summary>
    public List<string> ExcludedPathspecs { get; set; } =
    [
        "*.lock",
        "package-lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
        "*.min.js",
        "*.min.css",
        "*.map",
        "*.Designer.cs",
        "*.g.cs",
        "*.generated.cs",
        "*.svg",
        "*.png",
        "*.jpg",
        "*.ico",
        "*.dll",
        "*.exe",
        "*.bin",
        "*.wasm"
    ];
}