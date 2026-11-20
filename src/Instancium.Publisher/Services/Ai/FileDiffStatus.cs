namespace Instancium.Publisher.Services.Ai;

/// <summary>
/// Represents a single file path and its Git change status (ADDED, MODIFIED, DELETED, RENAMED)
/// extracted from a diff payload. Status accuracy drives prompt correctness: a modified file
/// must never be described as newly added in generated commit messages.
/// </summary>
internal sealed record FileDiffStatus(string Path, string Status);