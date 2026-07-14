using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class StructuredExtractionServiceTests
{
    [Fact]
    public async Task ExtractTableAsync_ValidJsonArray_ReturnsJsonAndCsv()
    {
        var chunks = MakeChunks("Nome,Idade\nAna,30\nBia,25");
        var llm = new FakeLlmProvider(_ => "```json\n[{\"nome\":\"Ana\",\"idade\":\"30\"},{\"nome\":\"Bia\",\"idade\":\"25\"}]\n```");
        var service = new StructuredExtractionService(_ => chunks, llm);

        var result = await service.ExtractTableAsync("doc.pdf", null);

        Assert.Contains("Ana", result.Json);
        Assert.Contains("nome,idade", result.Csv);
        Assert.Contains("Ana,30", result.Csv);
        Assert.Contains("Bia,25", result.Csv);
    }

    [Fact]
    public async Task ExtractTableAsync_TextWithoutFences_ExtractsArrayFromSurroundingText()
    {
        var chunks = MakeChunks("dados de teste");
        var llm = new FakeLlmProvider(_ => "Aqui está o resultado: [{\"a\":\"1\"}] espero que ajude");
        var service = new StructuredExtractionService(_ => chunks, llm);

        var result = await service.ExtractTableAsync("doc.pdf", null);

        Assert.Contains("\"a\"", result.Json);
    }

    [Fact]
    public async Task ExtractTableAsync_InvalidJson_ThrowsLlmJsonParseException()
    {
        var chunks = MakeChunks("dados de teste");
        var llm = new FakeLlmProvider(_ => "não consigo extrair nada útil daqui");
        var service = new StructuredExtractionService(_ => chunks, llm);

        await Assert.ThrowsAsync<LlmJsonParseException>(() => service.ExtractTableAsync("doc.pdf", null));
    }

    [Fact]
    public async Task ExtractTableAsync_DocumentNotIndexed_ThrowsInvalidOperationException()
    {
        var llm = new FakeLlmProvider(_ => "[]");
        var service = new StructuredExtractionService(_ => Array.Empty<SearchResultItem>(), llm);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExtractTableAsync("missing.pdf", null));
    }

    [Fact]
    public async Task ExtractTableAsync_WithInstruction_IncludesInstructionInPrompt()
    {
        var chunks = MakeChunks("tabela de preços e outra de contatos");
        string? capturedPrompt = null;
        var llm = new FakeLlmProvider(prompt =>
        {
            capturedPrompt = prompt;
            return "[{\"x\":\"1\"}]";
        });
        var service = new StructuredExtractionService(_ => chunks, llm);

        await service.ExtractTableAsync("doc.pdf", "apenas a tabela de preços");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("apenas a tabela de preços", capturedPrompt);
    }

    private static IReadOnlyList<SearchResultItem> MakeChunks(string text) => new[]
    {
        new SearchResultItem { SourcePath = "doc.pdf", FileName = "doc.pdf", ChunkIndex = 0, ChunkText = text, Score = 1f }
    };
}
