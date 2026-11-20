namespace Instancium.Publisher.Services.Ai;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Builds all commit-message prompts: the shared Map-Reduce warm-up prefix, per-chunk analysis
/// prompts, and the final Conventional Commit prompt. Prompt texts are business assets and are
/// preserved verbatim from the legacy assistant.
/// </summary>
internal static class CommitPromptBuilder
{
    internal const string MapReduceSystemPrompt = """
        You are an expert source code reviewer analyzing git diffs for a release commit.
        Your task is to analyze diff chunks and provide concise technical summaries.
        CRITICAL STATUS ACCURACY RULES:
        - If a file is marked [MODIFIED], describe changes/fixes to existing code. NEVER state that it was added or created.
        - Only describe a file as new if marked [ADDED].
        - Describe files marked [DELETED] as removed, and [RENAMED] as renamed or moved.
        - Provide 1-2 dense sentences without markdown fences or headers.
        """;

    internal static ChatMessage BuildChunkPrompt(string chunk, IReadOnlyList<FileDiffStatus> chunkStatuses)
    {
        string filesHeader = chunkStatuses.Count > 0
            ? string.Join("\n", chunkStatuses.Select(f => $"- [{f.Status}] {f.Path}"))
            : "- [MODIFIED] Unknown files";
        string prompt = $"""
            FILES IN THIS CHUNK WITH STATUS:
            {filesHeader}

            DIFF:
            {chunk}
            """;
        return new ChatMessage("user", prompt);
    }

    internal static ChatMessage BuildFinalPrompt(
        string aggregatedContext,
        string diffStat,
        IReadOnlyList<FileDiffStatus> fileStatuses)
    {
        string fileListText = BuildCompactFileRegistry(fileStatuses);
        string compactDiffStat = CompressDiffStat(diffStat);
        string statSection = string.IsNullOrWhiteSpace(compactDiffStat) ? string.Empty : $"""
            DIFF VOLUME STATISTICS:
            {compactDiffStat}
            """;
        string prompt = $"""
            You are an expert developer generating a Conventional Commit message.
            INPUT DATA:
            {fileListText}
            {statSection}
            CONTEXT:
            {aggregatedContext}
            INSTRUCTIONS:
            1. LANGUAGE: English ONLY.
            2. FORMAT:
               - Line 1: type(scope): description (Max 72 chars). Imperative mood.
               - Line 2: EMPTY.
               - Line 3+: Bullet points describing changes.
            3. CRITICAL RULES:
               - DO NOT use placeholders like "<no-body>" or "No changes".
               - If details are sparse, summarize the file changes based on the INPUT DATA.
               - NEVER say a file was "added" if status is [MODIFIED].
            EXAMPLE OUTPUT:
            feat(auth): implement jwt refresh token logic

            - Add RefreshToken method to AuthService
            - Update User model with token expiry field
            - Modify login endpoint to return refresh token
            YOUR TURN (Generate ONLY the commit message):
            """;
        return new ChatMessage("user", prompt);
    }

    private static string BuildCompactFileRegistry(IReadOnlyList<FileDiffStatus> fileStatuses)
    {
        const int maxFlatDisplay = 20;
        if (fileStatuses.Count <= maxFlatDisplay)
        {
            return string.Join("\n", fileStatuses.Select(f => $"- [{f.Status.ToUpperInvariant()}] {f.Path}"));
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Total files changed: {fileStatuses.Count}. Module Breakdown:");
        var groups = fileStatuses
            .GroupBy(f =>
            {
                var parts = f.Path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 2 && parts[0].Equals("src", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{parts[0]}/{parts[1]}";
                }
                return parts.Length > 1 ? parts[0] : "Root";
            })
            .OrderByDescending(g => g.Count());
        foreach (var group in groups)
        {
            int modifiedCount = group.Count(f => f.Status == "MODIFIED");
            int addedCount = group.Count(f => f.Status == "ADDED");
            int deletedCount = group.Count(f => f.Status == "DELETED");
            int renamedCount = group.Count(f => f.Status == "RENAMED");
            var statusParts = new List<string>();
            if (modifiedCount > 0) statusParts.Add($"{modifiedCount} modified");
            if (addedCount > 0) statusParts.Add($"{addedCount} added");
            if (deletedCount > 0) statusParts.Add($"{deletedCount} deleted");
            if (renamedCount > 0) statusParts.Add($"{renamedCount} renamed");
            sb.AppendLine($"- {group.Key}: {string.Join(", ", statusParts)}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string CompressDiffStat(string diffStat)
    {
        if (string.IsNullOrWhiteSpace(diffStat))
        {
            return string.Empty;
        }
        var lines = diffStat.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= 12)
        {
            return diffStat;
        }
        var topEntries = lines.Take(10);
        string summaryLine = lines.Last();
        var sb = new StringBuilder();
        foreach (var line in topEntries)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine($"... ({lines.Length - 11} more files)");
        sb.AppendLine(summaryLine);
        return sb.ToString().TrimEnd();
    }
}