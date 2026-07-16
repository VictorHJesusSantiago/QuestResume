using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class FaithfulnessCheckTests
{
    [Theory]
    [InlineData("SIM", true)]
    [InlineData("sim.", true)]
    [InlineData("Sim, a resposta é sustentada pelos trechos.", true)]
    [InlineData("NÃO", false)]
    [InlineData("nao", false)]
    [InlineData("Não, a resposta não é sustentada.", false)]
    [InlineData("", null)]
    [InlineData("resposta ambígua sem os tokens esperados", null)]
    public void ParseFaithfulnessResponse_DefensiveParsing_ReturnsExpectedVerdict(string response, bool? expected)
    {
        var result = RagQueryEngine.ParseFaithfulnessResponse(response);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseFaithfulnessResponse_WhicheverTokenAppearsFirst_Wins()
    {
        // "Não, mas na verdade sim" -> "não" appears before "sim" -> false.
        Assert.False(RagQueryEngine.ParseFaithfulnessResponse("Não, mas na verdade sim"));
    }

    [Fact]
    public async Task AskAsync_FaithfulnessCheckEnabled_PopulatesIsFaithfulAndConfidenceScore()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(prompt =>
            {
                if (prompt.Contains("sustentada pelos trechos"))
                {
                    return "SIM";
                }
                if (prompt.Contains("Perguntas relacionadas:"))
                {
                    return string.Empty;
                }
                return "Resposta principal.";
            });

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm, faithfulnessCheckEnabled: true);

            var result = await engine.AskAsync("pergunta de teste");

            Assert.Equal("Resposta principal.", result.Answer);
            Assert.True(result.IsFaithful);
            Assert.NotNull(result.ConfidenceScore);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_FaithfulnessCheckDisabled_LeavesIsFaithfulNull()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(_ => "Resposta principal.");

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm);

            var result = await engine.AskAsync("pergunta de teste");

            Assert.Null(result.IsFaithful);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_FaithfulnessCheckThrows_BestEffortLeavesIsFaithfulNullWithoutBreakingAnswer()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(prompt =>
            {
                if (prompt.Contains("sustentada pelos trechos"))
                {
                    throw new InvalidOperationException("Falha simulada do LLM ao checar fidelidade.");
                }
                return "Resposta principal.";
            });

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm, faithfulnessCheckEnabled: true);

            var result = await engine.AskAsync("pergunta de teste");

            Assert.Equal("Resposta principal.", result.Answer);
            Assert.Null(result.IsFaithful);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"faithfulness-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"faithfulness-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "Conteúdo de teste sobre fidelidade das respostas.");

        var indexer = new DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
