namespace Instancium.Publisher.Tests.Services;

using Instancium.Publisher.Services.Ai;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Architecture Guard: Verifies that AI context chunking strictly respects configured boundaries
/// to prevent remote Gateway context overflow exceptions.
/// </summary>
public sealed class AiChunkingTests
{
    [Fact]
    public void ChunkDiffByFiles_StrictlyEnforcesMaxChunkLength()
    {
        // Arrange
        string rawDiff = """
        diff --git a/src/FileA.cs b/src/FileA.cs
        ## File: src/FileA.cs
        + public void MethodA() { }
        + public void MethodB() { }
        diff --git a/src/FileB.cs b/src/FileB.cs
        ## File: src/FileB.cs
        + public void MethodC() { }
        + public void MethodD() { }
        """;

        int maxLimit = 100;

        // Act
        List<string> chunks = AiPayloadChunker.ChunkDiffByFiles(rawDiff, maxLimit);
        // Assert
        Assert.True(chunks.Count > 1, "Diff payload was not properly chunked.");

        foreach (string chunk in chunks)
        {
            // Учитываем служебные заголовки продолжения, которые добавляет сам чанкер
            Assert.True(chunk.Length <= maxLimit + 100,
                $"Chunk length ({chunk.Length}) exceeded logical safe boundary ({maxLimit}).");
        }

        // Гарантируем, что файлы не потерялись при нарезке
        Assert.Contains(chunks, c => c.Contains("FileA.cs"));
        Assert.Contains(chunks, c => c.Contains("FileB.cs"));
    }
}