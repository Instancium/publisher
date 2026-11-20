namespace Instancium.Publisher.Infrastructure.Ai;

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
        Preserve affected module/component names and concrete technical details.
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
        - FACTUALITY FIRST: Map ONLY actual changes present in the aggregated log. DO NOT invent or hallucinate changes.
        - GRANULARITY: Group changes by architectural `Domain` and `Vector`. Emit ONE row per logically distinct change or affected module. DO NOT collapse multiple unrelated changes or modules into a single generic row.
        - ATOMICITY: If the release genuinely contains only one atomic change, output 1 row; do NOT split a single atomic commit into multiple rows just to use different tags.
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

    private const char RecordSeparator = '\u001e';
    private const char UnitSeparator = '\u001f';

    /// <summary>
    /// Removes release-engineering commits atomically (header, body, and file stats together)
    /// when structured ASCII-delimited log records are supplied; falls back to line filtering
    /// for legacy plain-text logs.
    /// </summary>
    internal static string FilterReleaseNoise(string rawLog)
    {
        if (string.IsNullOrWhiteSpace(rawLog))
        {
            return rawLog;
        }

        if (!rawLog.Contains(RecordSeparator))
        {
            var lines = rawLog.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            return string.Join(Environment.NewLine, lines.Where(line => !IsReleaseNoise(line)));
        }

        var records = rawLog.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries);
        var output = new List<string>(records.Length);

        foreach (string record in records)
        {
            string[] fields = record.Split(UnitSeparator);
            if (fields.Length < 2)
            {
                continue;
            }

            string hash = fields[0].Trim();
            string subject = fields[1].Trim();
            string body = fields.Length >= 3 ? fields[2].Trim() : string.Empty;
            string stat = fields.Length >= 4 ? fields[3].Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(subject) || IsReleaseNoise(subject))
            {
                continue;
            }

            var entry = new System.Text.StringBuilder();
            entry.Append(string.IsNullOrEmpty(hash) ? subject : $"{hash} {subject}");

            if (!string.IsNullOrWhiteSpace(body))
            {
                entry.AppendLine();
                entry.Append(body);
            }

            if (!string.IsNullOrWhiteSpace(stat))
            {
                entry.AppendLine();
                entry.Append(stat);
            }

            output.Add(entry.ToString());
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", output);
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