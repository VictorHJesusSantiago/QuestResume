using QuestResume.Core.Indexing;

namespace QuestResume.Core.Tests;

public class AutoSummarizationTests
{
    [Fact]
    public async Task IndexFolderAsync_AutoSummarizationEnabled_PopulatesSummaryForIndexedFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"summarize-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"summarize-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo de teste para resumo automático.");

        try
        {
            var llm = new FakeLlmProvider(_ => "Este é um resumo automático de teste.");
            var indexer = new DocumentIndexer();

            await indexer.IndexFolderAsync(folder, indexPath, autoSummarizationEnabled: true, llmProvider: llm);

            var search = new SearchService(indexPath);
            var files = search.GetIndexedFiles();

            Assert.Single(files);
            Assert.Equal("Este é um resumo automático de teste.", files[0].Summary);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_AutoSummarizationDisabled_LeavesSummaryNull()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"nosummary-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"nosummary-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo sem resumo.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var files = search.GetIndexedFiles();

            Assert.Single(files);
            Assert.Null(files[0].Summary);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_SummarizationLlmFails_IndexingStillSucceeds()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"summarizefail-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"summarizefail-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo qualquer.");

        try
        {
            var llm = new FakeLlmProvider(new InvalidOperationException("LLM indisponível"));
            var indexer = new DocumentIndexer();

            var stats = await indexer.IndexFolderAsync(folder, indexPath, autoSummarizationEnabled: true, llmProvider: llm);

            Assert.Equal(1, stats.FilesProcessed);
            var search = new SearchService(indexPath);
            Assert.Single(search.GetIndexedFiles());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
