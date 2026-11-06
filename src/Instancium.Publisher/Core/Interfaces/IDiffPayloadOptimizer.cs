namespace Instancium.Publisher.Core.Interfaces;

/// <summary>
/// Defines the contract for sanitizing and reducing Git diff payloads to minimize LLM token consumption.
/// </summary>
public interface IDiffPayloadOptimizer
{
    /// <summary>
    /// Processes a raw Git diff string and returns an optimized, token-efficient representation.
    /// </summary>
    string Optimize(string rawDiff);
}
