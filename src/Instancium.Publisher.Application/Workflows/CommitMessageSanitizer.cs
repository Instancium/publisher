namespace Instancium.Publisher.Infrastructure.Ai;

using System;
using System.Linq;
using System.Text;

/// <summary>
/// Normalizes raw LLM output into a valid Conventional Commit message: strips placeholder
/// artifacts, enforces the 72-character header limit, and guarantees the mandatory blank line
/// between header and bullet body.
/// </summary>
public static class CommitMessageSanitizer
{
    public static string Sanitize(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return rawMessage;
        }
        string cleaned = rawMessage
            .Replace("<no-body>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</no-body>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("[no-body]", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(no body)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("No body provided.", string.Empty, StringComparison.OrdinalIgnoreCase);
        var lines = cleaned.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var contentLines = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (contentLines.Count == 0)
        {
            return string.Empty;
        }
        string title = contentLines[0].Trim();
        const int maxHeaderLength = 72;
        const int truncateAt = 69;
        if (title.Length > maxHeaderLength)
        {
            title = string.Concat(title.AsSpan(0, truncateAt), "...");
        }
        if (contentLines.Count <= 1)
        {
            return title;
        }
        var bodyLines = contentLines.Skip(1).ToList();
        while (bodyLines.Count > 0 && string.IsNullOrWhiteSpace(bodyLines[^1]))
        {
            bodyLines.RemoveAt(bodyLines.Count - 1);
        }
        if (bodyLines.Count == 0)
        {
            return title;
        }
        var builder = new StringBuilder();
        builder.AppendLine(title);
        builder.AppendLine();
        foreach (var line in bodyLines)
        {
            string cleanLine = line.TrimStart('`', '*', '-').Trim();
            if (!string.IsNullOrWhiteSpace(cleanLine))
            {
                if (!cleanLine.StartsWith("- ") && !cleanLine.StartsWith("* "))
                {
                    builder.AppendLine($"- {cleanLine}");
                }
                else
                {
                    builder.AppendLine(cleanLine);
                }
            }
        }
        return builder.ToString().TrimEnd();
    }
}