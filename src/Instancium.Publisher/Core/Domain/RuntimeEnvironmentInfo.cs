namespace Instancium.Publisher.Core.Domain;

/// <summary>
/// Immutable snapshot of the application runtime environment and AI engine metadata.
/// </summary>
public sealed record RuntimeEnvironmentInfo(
    string ApplicationVersion,
    string ModelId);