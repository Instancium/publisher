namespace Instancium.Publisher.Infrastructure.Ai;

using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Extracts per-file change statuses from raw Git diffs or optimizer-produced "## File:" headers.
/// Pure parsing logic, isolated from transport and prompt construction.
/// </summary>
internal static class GitDiffStatusParser
{
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
                if (currentFile != null)
                {
                    result.Add(new FileDiffStatus(currentFile, currentStatus));
                }
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
            if (line.StartsWith("## File: "))
            {
                if (currentFile != null)
                {
                    result.Add(new FileDiffStatus(currentFile, currentStatus));
                }
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
        if (currentFile != null)
        {
            result.Add(new FileDiffStatus(currentFile, currentStatus));
        }
        return result.DistinctBy(f => f.Path).ToList();
    }
}