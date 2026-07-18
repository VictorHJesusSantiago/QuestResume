using QuestResume.Core.Indexing;
using QuestResume.Core.Persistence;

namespace QuestResume.Core.Tests;

/// <summary>
/// Lote 8 — Sub-lote E1: verifica que a extração de entidades e o versionamento de documentos
/// rodam automaticamente como etapas pós-indexação de <see cref="DocumentIndexer.IndexFolderAsync"/>
/// quando habilitados, sem depender dos endpoints sob demanda.
/// </summary>
public class PostIndexingHooksTests
{
    [Fact]
    public async Task IndexFolderAsync_EntityExtractionEnabled_PopulatesEntityStore()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"entities-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"entities-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        var docPath = Path.Combine(folder, "doc.txt");
        await File.WriteAllTextAsync(docPath, "A empresa Acme contratou João em São Paulo.");

        try
        {
            var llm = new FakeLlmProvider(_ => "[{\"type\": \"empresa\", \"name\": \"Acme\"}, {\"type\": \"pessoa\", \"name\": \"João\"}]");
            var indexer = new DocumentIndexer();

            await indexer.IndexFolderAsync(folder, indexPath, entityExtractionEnabled: true, llmProvider: llm);

            var store = new EntityStore(indexPath);
            var entities = store.GetEntities(docPath);

            Assert.Contains(entities, e => e.Name == "Acme");
            Assert.Contains(entities, e => e.Name == "João");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_EntityExtractionDisabled_LeavesEntityStoreEmpty()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"noentities-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"noentities-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        var docPath = Path.Combine(folder, "doc.txt");
        await File.WriteAllTextAsync(docPath, "Texto qualquer.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var store = new EntityStore(indexPath);
            Assert.Empty(store.GetEntities(docPath));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_DocumentVersioningEnabled_SavesVersion()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"versions-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"versions-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        var docPath = Path.Combine(folder, "doc.txt");
        await File.WriteAllTextAsync(docPath, "Conteúdo versionado.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath, documentVersioningEnabled: true);

            var store = new DocumentVersionStore(indexPath);
            var versions = store.GetVersions(docPath);

            Assert.Single(versions);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task IndexFolderAsync_EntityExtractionLlmFails_IndexingStillSucceeds()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"entitiesfail-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"entitiesfail-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo qualquer.");

        try
        {
            var llm = new FakeLlmProvider(new InvalidOperationException("LLM indisponível"));
            var indexer = new DocumentIndexer();

            var stats = await indexer.IndexFolderAsync(folder, indexPath, entityExtractionEnabled: true, llmProvider: llm);

            Assert.Equal(1, stats.FilesProcessed);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
