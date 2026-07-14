using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class SummarizationServiceTests
{
    [Fact]
    public async Task SummarizeAsync_ReturnsTrimmedLlmResponse()
    {
        var llm = new FakeLlmProvider(_ => "  Resumo gerado pelo LLM.  ");
        var service = new SummarizationService(llm);

        var summary = await service.SummarizeAsync("doc.txt", "Conteúdo qualquer do documento.");

        Assert.Equal("Resumo gerado pelo LLM.", summary);
    }

    [Fact]
    public async Task SummarizeAsync_TruncatesLongInputBeforeSendingToLlm()
    {
        string? capturedPrompt = null;
        var llm = new FakeLlmProvider(prompt =>
        {
            capturedPrompt = prompt;
            return "Resumo.";
        });
        var service = new SummarizationService(llm);
        var longText = new string('a', 10_000);

        await service.SummarizeAsync("doc.txt", longText);

        Assert.NotNull(capturedPrompt);
        // O prompt inteiro (incluindo instruções) deve ser bem menor que o texto original de 10k
        // caracteres, confirmando que apenas os primeiros caracteres do documento foram enviados.
        Assert.True(capturedPrompt!.Length < longText.Length);
    }

    [Fact]
    public async Task SummarizeAsync_LlmFailure_Propagates()
    {
        var llm = new FakeLlmProvider(new InvalidOperationException("Falha simulada"));
        var service = new SummarizationService(llm);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SummarizeAsync("doc.txt", "conteúdo"));
    }
}
