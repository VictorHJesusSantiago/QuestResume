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

    [Fact]
    public void Chunk_NoPageMarkers_PageNumberIsNull()
    {
        var chunks = TextChunker.Chunk(MakeDocument("Texto simples sem marcadores de página."));

        Assert.All(chunks, c => Assert.Null(c.PageNumber));
    }

    [Fact]
    public void Chunk_WithPageMarkers_AssignsPageNumberPerPage()
    {
        // '\f' (form feed) is the page-boundary marker PdfExtractor inserts between pages. Each
        // page is a uniform run of one letter with no whitespace, and chunkSize exactly matches
        // each page's length, so FindBreakPoint (which only looks for whitespace) can't nudge
        // the chunk boundary off the page boundary — each chunk lines up exactly with one page.
        var text = new string('A', 100) + "\f" + new string('B', 100) + "\f" + new string('C', 100);

        var chunks = TextChunker.Chunk(MakeDocument(text), chunkSize: 100, overlap: 0);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(1, chunks[0].PageNumber);
        Assert.Equal(2, chunks[1].PageNumber);
        Assert.Equal(3, chunks[2].PageNumber);

        // The form-feed marker itself must not leak into the visible chunk text.
        Assert.DoesNotContain('\f', chunks[0].Text);
        Assert.DoesNotContain('\f', chunks[1].Text);
        Assert.DoesNotContain('\f', chunks[2].Text);
    }

    [Fact]
    public void Chunk_MultiplePagesInOneChunk_PageNumberIsWhereChunkStarts()
    {
        // A single chunk spanning two pages should report the page it *starts* on.
        var text = "A" + "\f" + "B";

        var chunks = TextChunker.Chunk(MakeDocument(text), chunkSize: 1000, overlap: 0);

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].PageNumber);
    }

    [Fact]
    public void SplitSentences_SplitsOnSentenceEndingPunctuation()
    {
        var sentences = TextChunker.SplitSentences("Primeira frase. Segunda frase! Terceira frase?");

        Assert.Equal(3, sentences.Count);
        Assert.Equal("Primeira frase.", sentences[0]);
        Assert.Equal("Segunda frase!", sentences[1]);
        Assert.Equal("Terceira frase?", sentences[2]);
    }

    [Fact]
    public void ChunkBySentences_OneChunkPerSentence()
    {
        var chunks = TextChunker.ChunkBySentences(MakeDocument("Primeira frase. Segunda frase. Terceira frase."), chunkSize: 1000);

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Primeira frase.", chunks[0].Text);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal(2, chunks[2].ChunkIndex);
    }

    [Fact]
    public void ChunkBySentences_SentenceLongerThanChunkSize_IsFurtherSplit()
    {
        var longSentence = string.Join(' ', Enumerable.Range(0, 300).Select(i => $"palavra{i}")) + ".";

        var chunks = TextChunker.ChunkBySentences(MakeDocument(longSentence), chunkSize: 500);

        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ChunkByHeadings_MarkdownDocument_SplitsAtHeadingBoundaries()
    {
        var doc = new ExtractedDocument
        {
            Path = "doc.md",
            FileName = "doc.md",
            Extension = ".md",
            Text = "# Título 1\nConteúdo da seção um.\n\n## Título 2\nConteúdo da seção dois."
        };

        var chunks = TextChunker.ChunkByHeadings(doc, chunkSize: 1000, overlap: 150);

        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("# Título 1", chunks[0].Text);
        Assert.StartsWith("## Título 2", chunks[1].Text);
    }

    [Fact]
    public void ChunkByHeadings_HtmlDocument_SplitsAtHeadingTags()
    {
        var doc = new ExtractedDocument
        {
            Path = "doc.html",
            FileName = "doc.html",
            Extension = ".html",
            Text = "<h1>Título 1</h1><p>Conteúdo um.</p><h2>Título 2</h2><p>Conteúdo dois.</p>"
        };

        var chunks = TextChunker.ChunkByHeadings(doc, chunkSize: 1000, overlap: 150);

        Assert.Equal(2, chunks.Count);
        Assert.Contains("Título 1", chunks[0].Text);
        Assert.Contains("Título 2", chunks[1].Text);
    }

    [Fact]
    public void ChunkByHeadings_SectionLargerThanChunkSize_IsFurtherSubdivided()
    {
        var bigSection = "# Título\n" + string.Join(' ', Enumerable.Range(0, 300).Select(i => $"palavra{i}"));
        var doc = new ExtractedDocument { Path = "doc.md", FileName = "doc.md", Extension = ".md", Text = bigSection };

        var chunks = TextChunker.ChunkByHeadings(doc, chunkSize: 500, overlap: 50);

        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ChunkParentChild_ChildChunksCarryParentText()
    {
        var text = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"palavra{i}"));

        var chunks = TextChunker.ChunkParentChild(MakeDocument(text), parentChunkSize: 1000, childChunkSize: 100);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.NotNull(c.ParentText));
        Assert.All(chunks, c => Assert.True(c.ParentText!.Length <= 1000));
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 100 || c.ParentText!.Length <= 100));

        // Every child's text must be a substring of its parent's text.
        Assert.All(chunks, c => Assert.Contains(c.Text, c.ParentText!));
    }

    [Fact]
    public void ChunkParentChild_MultipleParents_ChildrenGroupedByCorrectParent()
    {
        var text = string.Join(' ', Enumerable.Range(0, 800).Select(i => $"palavra{i}"));

        var chunks = TextChunker.ChunkParentChild(MakeDocument(text), parentChunkSize: 300, childChunkSize: 50);

        var distinctParents = chunks.Select(c => c.ParentText).Distinct().ToList();
        Assert.True(distinctParents.Count > 1);
    }

    [Fact]
    public void ChunkParentChild_EmptyText_ReturnsNoChunks()
    {
        var chunks = TextChunker.ChunkParentChild(MakeDocument("   "));

        Assert.Empty(chunks);
    }
}
