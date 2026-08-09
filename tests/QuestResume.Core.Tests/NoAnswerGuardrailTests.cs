using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class NoAnswerGuardrailTests
{
    [Fact]
    public async Task AskAsync_RelevanceBelowThreshold_ReturnsCannedAnswerWithoutCallingLlm()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            
            
            var llm = new FakeLlmProvider(new InvalidOperationException("O LLM não deveria ser chamado quando o guardrail está ativo."));

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm, minRelevanceThreshold: 1.0);

            var result = await engine.AskAsync("pergunta de teste sobre conteúdo indexado");

            Assert.Equal(RagQueryEngine.InsufficientContextAnswer, result.Answer);
            Assert.Null(result.IsFaithful);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_ThresholdDisabledByDefault_CallsLlmNormally()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(_ => "Resposta principal.");

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm);

            var result = await engine.AskAsync("pergunta de teste sobre conteúdo indexado");

            Assert.Equal("Resposta principal.", result.Answer);
            Assert.True(llm.CompleteCallCount > 0); 
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_NoResultsFound_GuardrailTripsWhenEnabled()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"guardrail-empty-index-{Guid.NewGuid()}");
        var folder = Path.Combine(Path.GetTempPath(), $"guardrail-empty-docs-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Algum conteúdo qualquer.");

        try
        {
            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(new InvalidOperationException("O LLM não deveria ser chamado."));

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm, minRelevanceThreshold: 0.1);

            
            
            var result = await engine.AskAsync("xyzabc123 termo completamente ausente do índice");

            Assert.Equal(RagQueryEngine.InsufficientContextAnswer, result.Answer);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"guardrail-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"guardrail-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo de teste sobre o guardrail de não sei.");

        var indexer = new DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
