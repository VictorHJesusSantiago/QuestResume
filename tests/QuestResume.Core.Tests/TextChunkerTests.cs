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

    [Fact]
    public void ChunkCode_EmptyText_ReturnsNoChunks()
    {
        var chunks = TextChunker.ChunkCode(MakeDocument("   "));

        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkCode_KeepsEachFunctionInItsOwnChunkWhenTheyFitTogether()
    {
        const string code = """
            public class Calculator
            {
                public int Add(int a, int b)
                {
                    return a + b;
                }

                public int Subtract(int a, int b)
                {
                    return a - b;
                }
            }
            """;

        var chunks = TextChunker.ChunkCode(MakeDocument(code), chunkSize: 1000, overlap: 100);

        Assert.Single(chunks);
        Assert.Contains("Add", chunks[0].Text);
        Assert.Contains("Subtract", chunks[0].Text);
    }

    [Fact]
    public void ChunkCode_SplitsAtFunctionBoundariesWhenChunkSizeIsSmall()
    {
        const string code = """
            def first_function():
                return "first function body with enough text to matter"

            def second_function():
                return "second function body with enough text to matter"
            """;

        var chunks = TextChunker.ChunkCode(MakeDocument(code), chunkSize: 80, overlap: 10);

        Assert.True(chunks.Count >= 2);
        Assert.Contains(chunks, c => c.Text.Contains("first_function"));
        Assert.Contains(chunks, c => c.Text.Contains("second_function"));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }

    [Fact]
    public void ChunkCode_OversizedFunction_FallsBackToSlidingWindow()
    {
        var bigBody = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"    print('line {i}')"));
        var code = $"def big_function():\n{bigBody}\n";

        var chunks = TextChunker.ChunkCode(MakeDocument(code), chunkSize: 200, overlap: 50);

        Assert.True(chunks.Count > 1);
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(c => c.ChunkIndex));
    }
}
