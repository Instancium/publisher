namespace Instancium.Publisher.Services.Ai;

using System;
using System.Linq;
using InstantAIGate.Core.Dtos.Inference;

/// <summary>
/// Builds release-notes prompts: the shared Map-Reduce warm-up prefix, the Instancium-signature
/// changelog prompt, and the release-noise filter. Prompt texts are business assets and are
/// preserved verbatim from the legacy assistant.
/// </summary>
internal static class ReleaseNotesPromptBuilder
{
    internal const string MapReduceSystemPrompt = """
        You are an expert technical writer analyzing git commit logs.
        Extract key features, bug fixes, and architectural changes from the provided log segments.
        Provide concise bullet points. Ignore trivial formatting commits.
        """;

    internal static ChatMessage BuildChangelogPrompt(string contextData)
    {
        string prompt = $"""
            Analyze the following aggregated commit data and generate an academic, structured release manifest.
            RULES:
            1. MUST be exclusively in English.
            2. DO NOT use generic lists or headers like "Features", "Bug Fixes", or "Chores".
            3. FORMAT REQUIREMENT (Instancium Signature):
               - Start with a single blockquote `> **Executive Summary**` followed by a new line with a 1-2 sentence high-level summary of the release impact.
               - Follow with an empty line.
               - Then, output a Markdown table with exactly these columns: | Vector | Domain | Modification |
               - Followed by a separator: | :--- | :--- | :--- |
            4. TABLE DATA RULES (CRITICAL):
               - FACTUALITY FIRST: Map ONLY actual changes present in the aggregated log. DO NOT invent, hallucinate, or pad the table. DO NOT split a single atomic commit into multiple rows just to use different tags.
               - It is expected and perfectly correct if the table contains only 1 row. Do NOT try to use every available vector tag.
               - 'Vector': Select the SINGLE most appropriate tag for the change from this list:
                 `Capability`
                 `Resolution`
                 `Refactoring`
                 `Maintenance`
               - 'Domain': Identify the specific architectural component or module affected (e.g., `SignalR`, `Pipeline`, `Core.Git`). Wrap it in inline code ticks (`).
               - 'Modification': A concise, technical description of the ACTUAL change. Start with an uppercase letter, end with a period.
            5. ABSOLUTELY IGNORE release engineering noise (version bumps, merges, chores related to release prep).
            6. Output ONLY valid Markdown. NO markdown fences (```). NO introductory or concluding text.
            Aggregated Log Data:
            {contextData}
            """;
        return new ChatMessage("user", prompt);
    }

    internal static string FilterReleaseNoise(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
        {
            return rawLog;
        }
        var lines = rawLog.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var filteredLines = lines.Where(line => !IsReleaseNoise(line));
        return string.Join(Environment.NewLine, filteredLines);
    }

    private static bool IsReleaseNoise(string line)
    {
        string lower = line.ToLowerInvariant();
        return lower.Contains("prepare version") ||
               lower.Contains("prepared version") ||
               lower.Contains("backport") ||
               lower.Contains("merge release") ||
               lower.Contains("chore(release)") ||
               lower.Contains("chore: merge") ||
               lower.Contains("chore: backport");
    }
}