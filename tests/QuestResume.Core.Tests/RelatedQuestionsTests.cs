using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class RelatedQuestionsTests
{
    [Fact]
    public async Task AskAsync_LlmReturnsRelatedQuestions_ParsesUpToThreeLines()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(prompt =>
                prompt.Contains("Perguntas relacionadas:")
                    ? "1. O que é X?\n- Como funciona Y?\n* Qual a diferença entre Z e W?\nLinha extra ignorada"
                    : "Resposta principal.");

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm);

            var result = await engine.AskAsync("pergunta de teste");

            Assert.Equal("Resposta principal.", result.Answer);
            Assert.Equal(3, result.RelatedQuestions.Count);
            Assert.Equal("O que é X?", result.RelatedQuestions[0]);
            Assert.Equal("Como funciona Y?", result.RelatedQuestions[1]);
            Assert.Equal("Qual a diferença entre Z e W?", result.RelatedQuestions[2]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_RelatedQuestionsGenerationFails_ReturnsEmptyListWithoutBreakingMainAnswer()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var callCount = 0;
            var llm = new FakeLlmProvider(prompt =>
            {
                callCount++;
                if (prompt.Contains("Perguntas relacionadas:"))
                {
                    throw new InvalidOperationException("Falha simulada do LLM ao gerar sugestões.");
                }
                return "Resposta principal.";
            });

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm);

            var result = await engine.AskAsync("pergunta de teste");

            Assert.Equal("Resposta principal.", result.Answer);
            Assert.Empty(result.RelatedQuestions);
            Assert.Equal(2, callCount); // resposta principal + tentativa de sugestões
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"related-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"related-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo de teste sobre perguntas e respostas.");

        var indexer = new QuestResume.Core.Indexing.DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
