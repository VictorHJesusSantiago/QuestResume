using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class TextChunkerTests
{
    private static ExtractedDocument MakeDocument(string text) => new()
    {
        Path = "doc.txt",
        FileName = "doc.txt",
        Extension = ".txt",
        Text = text
    };

    [Fact]
    public void Chunk_EmptyText_ReturnsNoChunks()
    {
        var chunks = TextChunker.Chunk(MakeDocument("   "));

        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var chunks = TextChunker.Chunk(MakeDocument("Hello world."), chunkSize: 1000, overlap: 150);

        Assert.Single(chunks);
        Assert.Equal("Hello world.", chunks[0].Text);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal("doc.txt", chunks[0].FileName);
    }

    [Fact]
    public void Chunk_LongText_SplitsIntoMultipleChunksWithOverlap()
    {
        var words = Enumerable.Range(0, 400).Select(i => $"word{i}");
        var text = string.Join(' ', words);

        var chunks = TextChunker.Chunk(MakeDocument(text), chunkSize: 200, overlap: 50);

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));

        // Consecutive chunks should overlap: the last word of one chunk should reappear
        // near the start of the next chunk.
        var lastWordOfFirstChunk = chunks[0].Text.Split(' ')[^1];
        Assert.Contains(lastWordOfFirstChunk, chunks[1].Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Chunk_InvalidChunkSize_Throws(int chunkSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk(MakeDocument("text"), chunkSize: chunkSize));
    }

    [Fact]
    public void Chunk_OverlapNotSmallerThanChunkSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk(MakeDocument("text"), chunkSize: 100, overlap: 100));
    }
}
