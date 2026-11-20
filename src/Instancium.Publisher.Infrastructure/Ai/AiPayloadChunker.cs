namespace Instancium.Publisher.Infrastructure.Ai;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Splits optimized diff and commit-log payloads into transport-safe chunks.
/// Diff chunking respects file boundaries, skips generated/binary payloads, and marks
/// cross-chunk continuations; log chunking splits strictly by lines.
/// Pure functions with no transport or inference concerns.
/// </summary>
internal static class AiPayloadChunker
{
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lock", ".min.js", ".min.css", ".map", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".ico",
        ".pdf", ".dll", ".so", ".exe", ".bin", ".gguf", ".zip", ".tar", ".gz", ".wasm",
        ".bundle.js", ".Designer.cs", ".g.cs"
    };

    private static readonly string[] IgnoredFileSubstrings =
    {
        "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "obj/Debug/", "obj/Release/",
        "bin/Debug/", "bin/Release/", ".min."
    };

    internal static List<string> ChunkDiffByFiles(string rawDiff, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(rawDiff);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        using var reader = new StringReader(rawDiff);
        string? line;
        bool isCurrentFileIgnored = false;
        string currentFileName = string.Empty;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.StartsWith("diff --git") || line.StartsWith("## File: "))
            {
                if (TryExtractFileHeader(line, out string extractedFileName))
                {
                    currentFileName = extractedFileName;
                    isCurrentFileIgnored = IsGeneratedOrBinary(currentFileName);

                    if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                    {
                        chunks.Add(currentChunk.ToString());
                        currentChunk.Clear();
                    }

                    if (isCurrentFileIgnored)
                    {
                        currentChunk.AppendLine(line);
                        currentChunk.AppendLine($"[SKIPPED: Auto-generated/binary payload for {currentFileName}]");
                        continue;
                    }
                }
            }
            if (!isCurrentFileIgnored)
            {
                if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxLength)
                {
                    chunks.Add(currentChunk.ToString());
                    currentChunk.Clear();
                    if (!string.IsNullOrEmpty(currentFileName))
                    {
                        currentChunk.AppendLine($"[... CONTINUATION OF DIFF FOR FILE: {currentFileName} ...]");
                    }
                }
                currentChunk.AppendLine(line);
            }
        }
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }
        return chunks;
    }


    private static bool TryExtractFileHeader(string line, out string fileName)
    {
        if (line.StartsWith("## File [", StringComparison.Ordinal))
        {
            int separatorIndex = line.IndexOf("]: ", StringComparison.Ordinal);
            if (separatorIndex > 0)
            {
                string rawPath = line[(separatorIndex + 3)..].Trim();
                int arrowIndex = rawPath.IndexOf(" -> ", StringComparison.Ordinal);
                fileName = arrowIndex >= 0 ? rawPath[(arrowIndex + 4)..].Trim() : rawPath;
                return true;
            }
        }

        if (line.StartsWith("## File: ", StringComparison.Ordinal))
        {
            fileName = line["## File: ".Length..].Trim();
            return true;
        }

        if (line.StartsWith("diff --git", StringComparison.Ordinal))
        {
            var parts = line.Split(' ');
            string targetPath = parts.Length >= 4 ? parts[3] : (parts.Length >= 3 ? parts[2] : string.Empty);
            targetPath = targetPath.Trim('"');
            fileName = targetPath.StartsWith("b/") || targetPath.StartsWith("a/")
                ? targetPath[2..]
                : targetPath;
            return true;
        }

        fileName = string.Empty;
        return false;
    }

    internal static List<string> ChunkStringByLines(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);

        var chunks = new List<string>();
        var currentChunk = new StringBuilder();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (currentChunk.Length > 0 && currentChunk.Length + line.Length > maxChars)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }
            currentChunk.AppendLine(line);
        }
        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString());
        }
        return chunks;
    }

    private static bool IsGeneratedOrBinary(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }
        string extension = Path.GetExtension(filePath);
        if (!string.IsNullOrEmpty(extension) && IgnoredExtensions.Contains(extension))
        {
            return true;
        }
        return IgnoredFileSubstrings.Any(sub => filePath.Contains(sub, StringComparison.OrdinalIgnoreCase));
    }
}