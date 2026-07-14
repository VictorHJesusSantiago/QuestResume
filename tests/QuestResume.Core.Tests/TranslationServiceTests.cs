using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task TranslateAsync_ReturnsTrimmedLlmResponse()
    {
        var llm = new FakeLlmProvider(_ => "  Hello, world!  \n");
        var service = new TranslationService(llm);

        var result = await service.TranslateAsync("Olá, mundo!", "en");

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task TranslateAsync_EmptyText_ReturnsEmptyWithoutCallingLlm()
    {
        var llm = new FakeLlmProvider(_ => throw new InvalidOperationException("não deveria ser chamado"));
        var service = new TranslationService(llm);

        var result = await service.TranslateAsync("   ", "en");

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, llm.CompleteCallCount);
    }

    [Fact]
    public async Task TranslateAsync_IncludesTargetLanguageInPrompt()
    {
        string? capturedPrompt = null;
        var llm = new FakeLlmProvider(prompt =>
        {
            capturedPrompt = prompt;
            return "Bonjour";
        });
        var service = new TranslationService(llm);

        await service.TranslateAsync("Olá", "fr");

        Assert.NotNull(capturedPrompt);
        Assert.Contains("fr", capturedPrompt);
    }
}
