namespace Instancium.Publisher.Services.Ai;

using System;
using System.Text;
using System.Text.RegularExpressions;
using Instancium.Publisher.Configuration;
using Instancium.Publisher.Core.Interfaces;
using Microsoft.Extensions.Options;

/// <summary>
/// Optimizes diff payloads using regex to strip verbose Git headers and truncate massive hunks.
/// Operates on the principle of reducing attention dilution for the LLM.
/// </summary>
public sealed partial class RegexDiffPayloadOptimizer : IDiffPayloadOptimizer
{
    private readonly DiffOptimizationSettings _settings;

    [GeneratedRegex(@"^diff --git a/(?<old>.*?) b/(?<new>.*?)\r?\n(?:old mode .*\r?\n)?(?:new mode .*\r?\n)?(?:deleted file mode .*\r?\n)?(?:similarity index .*\r?\n)?(?:rename from .*\r?\n)?(?:rename to .*\r?\n)?(?:index .*\r?\n)?(?:--- a/.*\r?\n)?(?:\+\+\+ b/.*\r?\n)?", RegexOptions.Multiline | RegexOptions.Compiled)]
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
            string fileName = match.Groups["new"].Success && match.Groups["new"].Value != "/dev/null"
                ? match.Groups["new"].Value
                : match.Groups["old"].Value;

            return $"## File: {fileName}\n";
        });
    }

    private static string TruncateLargeHunks(string diff, int maxLines)
    {
        if (maxLines <= 0) return diff;

        var lines = diff.Split('\n');
        var sb = new StringBuilder(diff.Length);
        int currentHunkSize = 0;
        bool inTruncation = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@ "))
            {
                currentHunkSize = 0;
                inTruncation = false;
                sb.AppendLine(line);
                continue;
            }

            if (line.StartsWith("## File:"))
            {
                currentHunkSize = 0;
                inTruncation = false;
                sb.AppendLine(line);
                continue;
            }

            currentHunkSize++;

            if (currentHunkSize <= maxLines)
            {
                sb.AppendLine(line);
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
