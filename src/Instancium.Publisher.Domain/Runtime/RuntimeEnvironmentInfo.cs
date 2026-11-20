namespace Instancium.Publisher.Domain.Runtime;

/// <summary>
/// Immutable snapshot of the application runtime environment and AI engine metadata.
/// </summary>
public sealed record RuntimeEnvironmentInfo(
    string ApplicationVersion,
    string ModelId);