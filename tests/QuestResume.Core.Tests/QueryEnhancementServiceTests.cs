using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class QueryEnhancementServiceTests
{
    [Fact]
    public async Task ExpandQueryAsync_ParsesOneTermPerLine()
    {
        var llm = new FakeLlmProvider(_ => "carro\nautomóvel\nveículo");
        var service = new QueryEnhancementService(llm);

        var terms = await service.ExpandQueryAsync("carro usado");

        Assert.Equal(new[] { "carro", "automóvel", "veículo" }, terms);
    }

    [Fact]
    public async Task ExpandQueryAsync_StripsBulletsAndNumbering()
    {
        var llm = new FakeLlmProvider(_ => "1. carro\n- automóvel\n* veículo");
        var service = new QueryEnhancementService(llm);

        var terms = await service.ExpandQueryAsync("carro");

        Assert.Equal(new[] { "carro", "automóvel", "veículo" }, terms);
    }

    [Fact]
    public async Task ExpandQueryAsync_CapsAtThreeTerms()
    {
        var llm = new FakeLlmProvider(_ => "um\ndois\ntres\nquatro\ncinco");
        var service = new QueryEnhancementService(llm);

        var terms = await service.ExpandQueryAsync("qualquer coisa");

        Assert.Equal(3, terms.Count);
    }

    [Fact]
    public async Task GenerateHypotheticalAnswerAsync_ReturnsTrimmedText()
    {
        var llm = new FakeLlmProvider(_ => "  Uma resposta hipotética.  ");
        var service = new QueryEnhancementService(llm);

        var answer = await service.GenerateHypotheticalAnswerAsync("O que é X?");

        Assert.Equal("Uma resposta hipotética.", answer);
    }

    [Fact]
    public async Task GenerateHypotheticalAnswerAsync_EmptyResponse_ReturnsNull()
    {
        var llm = new FakeLlmProvider(_ => "   ");
        var service = new QueryEnhancementService(llm);

        var answer = await service.GenerateHypotheticalAnswerAsync("O que é X?");

        Assert.Null(answer);
    }

    [Fact]
    public async Task GenerateQueryVariationsAsync_ParsesUpToRequestedCount()
    {
        var llm = new FakeLlmProvider(_ => "Variação um\nVariação dois\nVariação três\nVariação quatro");
        var service = new QueryEnhancementService(llm);

        var variations = await service.GenerateQueryVariationsAsync("pergunta original", count: 2);

        Assert.Equal(2, variations.Count);
    }

    [Fact]
    public async Task GenerateShortDocumentContextAsync_ReturnsTrimmedSummary()
    {
        var llm = new FakeLlmProvider(_ => "  Contrato de prestação de serviços.  ");
        var service = new QueryEnhancementService(llm);

        var context = await service.GenerateShortDocumentContextAsync("contrato.txt", "texto longo do documento...");

        Assert.Equal("Contrato de prestação de serviços.", context);
    }

    [Fact]
    public async Task ExpandQueryAsync_LlmThrows_PropagatesForCallerToHandleBestEffort()
    {
        var llm = new FakeLlmProvider(new InvalidOperationException("modelo indisponível"));
        var service = new QueryEnhancementService(llm);

        // QueryEnhancementService itself doesn't swallow exceptions — best-effort handling is the
        // caller's responsibility (HybridSearchService/DocumentIndexer), matching the pattern used
        // by SummarizationService.SummarizeAsync elsewhere in the codebase.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpandQueryAsync("pergunta"));
    }
}
