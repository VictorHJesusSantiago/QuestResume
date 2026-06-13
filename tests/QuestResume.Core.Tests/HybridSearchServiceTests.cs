using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class HybridSearchServiceTests
{
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
