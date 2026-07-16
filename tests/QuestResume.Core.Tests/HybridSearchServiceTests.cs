using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class HybridSearchServiceTests
{
    [Fact]
    public void CombineRrf_ItemRankedFirstInBothLists_ScoresHighest()
    {
        var hybrid = new HybridSearchService(searchService: null!, rankFusionStrategy: "Rrf", rrfK: 60);

        var a = MakeItem("a.txt", score: 10f);
        var b = MakeItem("b.txt", score: 1f);
        var c = MakeItem("c.txt", score: 5f);

        // "a" ranks first in both lists; "b" only appears in the second (with a low BM25-style
        // score that would lose under linear combination, but RRF only cares about rank).
        var bm25List = new[] { a, c };
        var vectorList = new[] { a, b };

        var combined = hybrid.CombineRrf(new[] { bm25List, vectorList }, topK: 3);

        // "a" appears at rank 1 in both lists (RRF score 1/61 + 1/61 = 2/61 ≈ 0.0328), beating
        // "c" (rank 2 in one list only, 1/62 ≈ 0.0161) and "b" (rank 2 in one list only), even
        // though "a"'s own BM25-style Score field (10) isn't itself the RRF ranking score —
        // CombineRrf ranks purely by reciprocal rank, not by the original per-list scores.
        Assert.Equal("a.txt", combined[0].SourcePath);
        Assert.Equal(3, combined.Count);
    }

    [Fact]
    public void CombineRrf_DeduplicatesBySourcePathAndChunkIndex()
    {
        var hybrid = new HybridSearchService(searchService: null!, rankFusionStrategy: "Rrf");

        var a = MakeItem("a.txt", score: 1f, chunkIndex: 0);
        var aDuplicate = MakeItem("a.txt", score: 99f, chunkIndex: 0);

        var combined = hybrid.CombineRrf(new[] { new[] { a }, new[] { aDuplicate } }, topK: 10);

        Assert.Single(combined);
    }

    private static SearchResultItem MakeItem(string path, float score, int chunkIndex = 0) => new()
    {
        SourcePath = path,
        FileName = path,
        ChunkIndex = chunkIndex,
        ChunkText = "texto",
        Score = score
    };

    [Fact]
    public async Task SearchAsync_WithoutVectorStore_DelegatesToBm25()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var hybrid = new HybridSearchService(search);

            var bm25Results = search.Search("perguntas e respostas", topK: 5);
            var hybridResults = await hybrid.SearchAsync("perguntas e respostas", topK: 5);

            Assert.Equal(bm25Results.Count, hybridResults.Count);
            Assert.Equal(bm25Results[0].SourcePath, hybridResults[0].SourcePath);
            Assert.Equal(bm25Results[0].Score, hybridResults[0].Score);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_EmbeddingsNotConfigured_FallsBackToBm25()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            using var vectorStore = new VectorStore(indexPath);
            using var embeddingService = new EmbeddingService(modelPath: string.Empty, vocabPath: string.Empty);
            var hybrid = new HybridSearchService(search, vectorStore, embeddingService);

            var bm25Results = search.Search("perguntas e respostas", topK: 5);
            var hybridResults = await hybrid.SearchAsync("perguntas e respostas", topK: 5);

            Assert.Equal(bm25Results.Count, hybridResults.Count);
            Assert.Equal(bm25Results[0].SourcePath, hybridResults[0].SourcePath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_QueryExpansionEnabled_IncludesLlmSuggestedTermsInBm25Query()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);

            // "zzznadaaqui" alone doesn't match anything in the indexed text, but the LLM
            // "expands" it to "perguntas", which does.
            var baseline = search.Search("zzznadaaqui", topK: 5);
            Assert.Empty(baseline);

            var llm = new FakeLlmProvider(_ => "perguntas");
            var expanded = new HybridSearchService(
                search, llmFactory: _ => Task.FromResult<ILlmProvider>(llm), queryExpansionEnabled: true);
            var results = await expanded.SearchAsync("zzznadaaqui", topK: 5);

            Assert.NotEmpty(results);
            Assert.Equal(1, llm.CompleteCallCount);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_QueryExpansionLlmFails_FallsBackToNormalSearch()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(new InvalidOperationException("LLM indisponível"));
            var hybrid = new HybridSearchService(
                search, llmFactory: _ => Task.FromResult<ILlmProvider>(llm), queryExpansionEnabled: true);

            // Best-effort: LLM failure during expansion must not break the underlying search.
            var results = await hybrid.SearchAsync("perguntas e respostas", topK: 5);

            Assert.NotEmpty(results);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_MultiQueryEnabled_MergesResultsAcrossVariations()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"multiquery-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"multiquery-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "gatos.txt"), "O gato é um animal doméstico independente.");
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);

            // The original question ("zzznadaaqui") matches nothing; the LLM-generated variation
            // ("gato") does — multi-query retrieval should still surface it.
            var llm = new FakeLlmProvider(_ => "gato");
            var hybrid = new HybridSearchService(
                search, llmFactory: _ => Task.FromResult<ILlmProvider>(llm), multiQueryEnabled: true, multiQueryVariations: 1);

            var results = await hybrid.SearchAsync("zzznadaaqui", topK: 5);

            Assert.NotEmpty(results);
            Assert.Contains(results, r => r.FileName == "gatos.txt");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task SearchAsync_HydeEnabled_BestEffortFallsBackWhenEmbeddingsNotConfigured()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            using var vectorStore = new VectorStore(indexPath);
            using var embeddingService = new EmbeddingService(modelPath: string.Empty, vocabPath: string.Empty);

            var llm = new FakeLlmProvider(_ => "Uma resposta hipotética sobre perguntas e respostas.");
            var hybrid = new HybridSearchService(
                search, vectorStore, embeddingService,
                llmFactory: _ => Task.FromResult<ILlmProvider>(llm), hydeEnabled: true);

            var results = await hybrid.SearchAsync("perguntas e respostas", topK: 5);

            // HyDE generation ran (proves wiring), but with no usable embedding model configured
            // the vector step best-effort falls back to BM25-only, same as without HyDE.
            Assert.Equal(1, llm.CompleteCallCount);
            Assert.NotEmpty(results);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"hybrid-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"hybrid-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(
            Path.Combine(folder, "doc.txt"),
            "QuestResume é um sistema de perguntas e respostas offline sobre documentos locais.");

        var indexer = new DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
