using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class SearchQualityIndexingTests
{
    [Fact]
    public async Task IndexFolderAsync_ContextualRetrievalEnabled_PrefixesEmbeddingTextButNotStoredChunk()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"context-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"context-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Texto curto de um documento qualquer.");

        try
        {
            var llm = new FakeLlmProvider(_ => "Contrato de prestação de serviços.");
            var embeddingService = new FakeEmbeddingService(new[] { "contrato" });
            using var vectorStore = new VectorStore(indexPath);

            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);
            await indexer.IndexFolderAsync(
                folder, indexPath, contextualRetrievalEnabled: true, llmProvider: llm);

            
            
            Assert.Contains(embeddingService.EmbeddedTexts, t => t.Contains("Contrato de prestação de serviços."));

            
            var search = new SearchService(indexPath);
            var chunks = search.GetChunksByPath(Path.Combine(folder, "doc.txt"));
            Assert.Single(chunks);
            Assert.Equal("Texto curto de um documento qualquer.", chunks[0].ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_ContextualRetrievalLlmFails_IndexingStillSucceeds()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"context-fail-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"context-fail-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Texto qualquer.");

        try
        {
            var llm = new FakeLlmProvider(new InvalidOperationException("LLM indisponível"));
            var indexer = new DocumentIndexer();

            var stats = await indexer.IndexFolderAsync(folder, indexPath, contextualRetrievalEnabled: true, llmProvider: llm);

            Assert.Equal(1, stats.FilesProcessed);
            Assert.Empty(stats.Errors);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_ParentChildChunkingEnabled_SearchReturnsParentText()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"parentchild-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"parentchild-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        var text = "Frase inicial sobre gatos. " + string.Join(' ', Enumerable.Range(0, 100).Select(i => $"enchimento{i}"));
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), text);

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(
                folder, indexPath, chunkSize: 1000, overlap: 0,
                parentChildChunkingEnabled: true, parentChunkSize: 800, childChunkSize: 50);

            var search = new SearchService(indexPath);
            var results = search.Search("gatos", topK: 5);

            Assert.NotEmpty(results);
            
            
            
            Assert.True(results[0].ChunkText.Length > 50);
            Assert.Contains("enchimento", results[0].ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_SemanticChunkingEnabled_GroupsSimilarSentencesTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"semantic-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"semantic-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        
        
        
        var text = "O gato dorme muito. O gato caça ratos. O carro é rápido. O carro precisa de gasolina.";
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), text);

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato", "carro" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);

            await indexer.IndexFolderAsync(
                folder, indexPath, semanticChunkingEnabled: true, semanticChunkingThreshold: 0.6);

            var search = new SearchService(indexPath);
            var chunks = search.GetChunksByPath(Path.Combine(folder, "doc.txt"));

            
            Assert.Equal(2, chunks.Count);
            Assert.Contains("gato", chunks[0].ChunkText);
            Assert.DoesNotContain("carro", chunks[0].ChunkText);
            Assert.Contains("carro", chunks[1].ChunkText);
            Assert.DoesNotContain("gato", chunks[1].ChunkText);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_SemanticDeduplicationEnabled_FlagsNearDuplicateDocuments()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"dedup-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"dedup-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        
        
        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "O gato dorme. O gato caça. O gato brinca.");
        await File.WriteAllTextAsync(Path.Combine(folder, "b.txt"), "O gato dorme muito. O gato caça ratos também.");

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);

            var stats = await indexer.IndexFolderAsync(
                folder, indexPath, semanticDeduplicationEnabled: true, semanticDuplicateThreshold: 0.9);

            Assert.Single(stats.NearDuplicates);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_SemanticDeduplicationEnabled_DoesNotFlagDissimilarDocuments()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"nodedup-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"nodedup-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);

        await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "O gato dorme. O gato caça. O gato brinca.");
        await File.WriteAllTextAsync(Path.Combine(folder, "b.txt"), "O carro é rápido. O carro precisa de gasolina.");

        try
        {
            var embeddingService = new FakeEmbeddingService(new[] { "gato", "carro" });
            using var vectorStore = new VectorStore(indexPath);
            var indexer = new DocumentIndexer(embeddingService: embeddingService, vectorStore: vectorStore);

            var stats = await indexer.IndexFolderAsync(
                folder, indexPath, semanticDeduplicationEnabled: true, semanticDuplicateThreshold: 0.9);

            Assert.Empty(stats.NearDuplicates);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
