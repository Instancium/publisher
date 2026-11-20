namespace Instancium.Publisher.Infrastructure.Configuration;

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
