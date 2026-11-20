namespace Instancium.Publisher.Infrastructure.Ai;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

internal static partial class GitDiffStatusParser
{
    [GeneratedRegex(@"^## File \[(?<status>ADDED|MODIFIED|DELETED|RENAMED)\]:\s*(?<path>.+)$", RegexOptions.Compiled)]
    private static partial Regex OptimizedHeaderRegex();

    internal static List<FileDiffStatus> ExtractFileStatuses(string diffText)
    {
        var result = new List<FileDiffStatus>();
        using var reader = new StringReader(diffText);
        string? line;
        string? currentFile = null;
        string currentStatus = "MODIFIED";

        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git a"))
            {
                FlushCurrent(result, ref currentFile, currentStatus);
                var parts = line.Split(' ');
                if (parts.Length >= 4 && parts[3].StartsWith("b/"))
                {
                    currentFile = parts[3][2..];
                }
                else if (parts.Length >= 3 && parts[2].StartsWith("a/"))
                {
                    currentFile = parts[2][2..];
                }

                currentStatus = "MODIFIED";
                continue;
            }

            var optimizedMatch = OptimizedHeaderRegex().Match(line);
            if (optimizedMatch.Success)
            {
                FlushCurrent(result, ref currentFile, currentStatus);
                currentStatus = optimizedMatch.Groups["status"].Value;
                currentFile = optimizedMatch.Groups["path"].Value.Trim();
                continue;
            }

            if (line.StartsWith("## File: "))
            {
                FlushCurrent(result, ref currentFile, currentStatus);
                currentFile = line["## File: ".Length..].Trim();
                currentStatus = "MODIFIED";
                continue;
            }

            if (line.StartsWith("new file mode"))
            {
                currentStatus = "ADDED";
            }
            else if (line.StartsWith("deleted file mode"))
            {
                currentStatus = "DELETED";
            }
            else if (line.StartsWith("similarity index") || line.StartsWith("rename from"))
            {
                currentStatus = "RENAMED";
            }
        }

        FlushCurrent(result, ref currentFile, currentStatus);
        return result.DistinctBy(f => f.Path).ToList();
    }

    private static void FlushCurrent(List<FileDiffStatus> result, ref string? currentFile, string currentStatus)
    {
        if (currentFile != null)
        {
            result.Add(new FileDiffStatus(currentFile, currentStatus));
            currentFile = null;
        }
    }
}