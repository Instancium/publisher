namespace Instancium.Publisher.Infrastructure.Ai;

using Instancium.Publisher.Application.Abstractions;
using Instancium.Publisher.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Strips verbose Git diff metadata while preserving semantic file statuses
/// ([ADDED], [MODIFIED], [DELETED], [RENAMED]) so downstream Map-Reduce chunks
/// retain accurate change classifications.
/// </summary>
public sealed partial class RegexDiffPayloadOptimizer : IDiffPayloadOptimizer
{
    private readonly DiffOptimizationSettings _settings;

    [GeneratedRegex(
        @"^diff --git a/(?<old>.*?) b/(?<new>.*?)\r?\n" +
        @"(?:old mode .*\r?\n)?" +
        @"(?:new mode .*\r?\n)?" +
        @"(?<newFile>new file mode .*\r?\n)?" +
        @"(?<deletedFile>deleted file mode .*\r?\n)?" +
        @"(?<similarity>similarity index .*\r?\n)?" +
        @"(?:rename from (?<renameFrom>.*?)\r?\n)?" +
        @"(?:rename to (?<renameTo>.*?)\r?\n)?" +
        @"(?:index .*\r?\n)?" +
        @"(?:--- (?:a/.*|/dev/null)\r?\n)?" +
        @"(?:\+\+\+ (?:b/.*|/dev/null)\r?\n)?",
        RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex GitHeaderRegex();

    public RegexDiffPayloadOptimizer(IOptions<PublisherSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.Value.DiffOptimization;
    }

    public string Optimize(string rawDiff)
    {
        if (string.IsNullOrWhiteSpace(rawDiff) || !_settings.Enabled)
        {
            return rawDiff;
        }

        string headersStripped = StripGitHeaders(rawDiff);
        return TruncateLargeHunks(headersStripped, _settings.MaxHunkLines);
    }

    private static string StripGitHeaders(string diff)
    {
        return GitHeaderRegex().Replace(diff, match =>
        {
            string oldPath = match.Groups["old"].Value.Trim();
            string newPath = match.Groups["new"].Value.Trim();

            if (match.Groups["newFile"].Success)
            {
                return $"## File [ADDED]: {newPath}\n";
            }

            if (match.Groups["deletedFile"].Success)
            {
                return $"## File [DELETED]: {oldPath}\n";
            }

            if (match.Groups["renameFrom"].Success || match.Groups["renameTo"].Success || match.Groups["similarity"].Success)
            {
                string from = match.Groups["renameFrom"].Success ? match.Groups["renameFrom"].Value.Trim() : oldPath;
                string to = match.Groups["renameTo"].Success ? match.Groups["renameTo"].Value.Trim() : newPath;
                return $"## File [RENAMED]: {from} -> {to}\n";
            }

            string fileName = !string.IsNullOrEmpty(newPath) && newPath != "/dev/null" ? newPath : oldPath;
            return $"## File [MODIFIED]: {fileName}\n";
        });
    }

    private static string TruncateLargeHunks(string diff, int maxLines)
    {
        if (maxLines <= 0)
        {
            return diff;
        }

        var lines = diff.Split('\n');
        var sb = new StringBuilder(diff.Length);
        int currentHunkSize = 0;
        bool inTruncation = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@ ") || line.StartsWith("## File"))
            {
                currentHunkSize = 0;
                inTruncation = false;
                sb.AppendLine(line.TrimEnd('\r'));
                continue;
            }

            currentHunkSize++;
            if (currentHunkSize <= maxLines)
            {
                sb.AppendLine(line.TrimEnd('\r'));
            }
            else if (!inTruncation)
            {
                sb.AppendLine($"\n[... Truncated hunk: exceeded {maxLines} lines to preserve LLM context ...]\n");
                inTruncation = true;
            }
        }

        return sb.ToString().TrimEnd();
    }
}