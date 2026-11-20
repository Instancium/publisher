namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Infrastructure.Ai;
using Instancium.Publisher.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Verifies that diff optimization preserves ADDED, DELETED, RENAMED, and MODIFIED
/// statuses and that downstream chunking and status parsing reconstruct them accurately.
/// </summary>
public sealed class RegexDiffPayloadOptimizerTests
{
    private static RegexDiffPayloadOptimizer CreateOptimizer(int maxHunkLines = 80)
    {
        var settings = Options.Create(new PublisherSettings
        {
            DiffOptimization = new DiffOptimizationSettings
            {
                Enabled = true,
                MaxHunkLines = maxHunkLines
            }
        });

        return new RegexDiffPayloadOptimizer(settings);
    }

    [Fact]
    public void Optimize_PreservesAllGitFileStatusesAcrossChunkingAndStatusParsing()
    {
        const string rawDiff = """
            diff --git a/src/NewService.cs b/src/NewService.cs
            new file mode 100644
            index 0000000..1111111
            --- /dev/null
            +++ b/src/NewService.cs
            @@ -0,0 +1,2 @@
            +public sealed class NewService { }
            diff --git a/src/Obsolete.cs b/src/Obsolete.cs
            deleted file mode 100644
            index 2222222..0000000
            --- a/src/Obsolete.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -public sealed class Obsolete { }
            diff --git a/src/OldName.cs b/src/NewName.cs
            similarity index 95%
            rename from src/OldName.cs
            rename to src/NewName.cs
            index 3333333..4444444 100644
            --- a/src/OldName.cs
            +++ b/src/NewName.cs
            @@ -1 +1 @@
            -class OldName {}
            +class NewName {}
            diff --git a/src/Existing.cs b/src/Existing.cs
            index 5555555..6666666 100644
            --- a/src/Existing.cs
            +++ b/src/Existing.cs
            @@ -10,2 +10,3 @@
             void Run()
            +    => Execute();
            """;

        RegexDiffPayloadOptimizer optimizer = CreateOptimizer();

        string optimized = optimizer.Optimize(rawDiff);
        List<string> chunks = AiPayloadChunker.ChunkDiffByFiles(optimized, maxLength: 180);
        var parsedStatuses = new List<FileDiffStatus>();
        foreach (string chunk in chunks)
        {
            parsedStatuses.AddRange(GitDiffStatusParser.ExtractFileStatuses(chunk));
        }

        Assert.Contains(parsedStatuses, s => s.Path == "src/NewService.cs" && s.Status == "ADDED");
        Assert.Contains(parsedStatuses, s => s.Path == "src/Obsolete.cs" && s.Status == "DELETED");
        Assert.Contains(parsedStatuses, s => s.Path == "src/OldName.cs -> src/NewName.cs" && s.Status == "RENAMED");
        Assert.Contains(parsedStatuses, s => s.Path == "src/Existing.cs" && s.Status == "MODIFIED");
    }
}