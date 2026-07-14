using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class FlashcardServiceTests
{
    [Fact]
    public async Task GenerateFlashcardsAsync_ValidJson_ReturnsFlashcards()
    {
        var chunks = MakeChunks("O céu é azul. A grama é verde.");
        var llm = new FakeLlmProvider(_ =>
            "[{\"question\":\"De que cor é o céu?\",\"answer\":\"Azul\"}," +
            "{\"question\":\"De que cor é a grama?\",\"answer\":\"Verde\"}]");
        var service = new FlashcardService(_ => chunks, llm);

        var cards = await service.GenerateFlashcardsAsync("doc.txt", 2);

        Assert.Equal(2, cards.Count);
        Assert.Equal("De que cor é o céu?", cards[0].Question);
        Assert.Equal("Azul", cards[0].Answer);
    }

    [Fact]
    public async Task GenerateFlashcardsAsync_InvalidJson_ThrowsLlmJsonParseException()
    {
        var chunks = MakeChunks("conteúdo");
        var llm = new FakeLlmProvider(_ => "isso não é json algum");
        var service = new FlashcardService(_ => chunks, llm);

        await Assert.ThrowsAsync<LlmJsonParseException>(() => service.GenerateFlashcardsAsync("doc.txt", 3));
    }

    [Fact]
    public async Task GenerateFlashcardsAsync_EmptyArray_ThrowsLlmJsonParseException()
    {
        var chunks = MakeChunks("conteúdo");
        var llm = new FakeLlmProvider(_ => "[]");
        var service = new FlashcardService(_ => chunks, llm);

        await Assert.ThrowsAsync<LlmJsonParseException>(() => service.GenerateFlashcardsAsync("doc.txt", 3));
    }

    [Fact]
    public async Task GenerateQuizAsync_ValidJson_ReturnsQuizQuestions()
    {
        var chunks = MakeChunks("Paris é a capital da França.");
        var llm = new FakeLlmProvider(_ =>
            "[{\"question\":\"Capital da França?\",\"options\":[\"Paris\",\"Roma\",\"Berlim\",\"Madri\"],\"correctOptionIndex\":0}]");
        var service = new FlashcardService(_ => chunks, llm);

        var questions = await service.GenerateQuizAsync("doc.txt", 1);

        Assert.Single(questions);
        Assert.Equal(4, questions[0].Options.Count);
        Assert.Equal(0, questions[0].CorrectOptionIndex);
    }

    [Fact]
    public async Task GenerateQuizAsync_DocumentNotIndexed_ThrowsInvalidOperationException()
    {
        var llm = new FakeLlmProvider(_ => "[]");
        var service = new FlashcardService(_ => Array.Empty<SearchResultItem>(), llm);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateQuizAsync("missing.txt", 3));
    }

    private static IReadOnlyList<SearchResultItem> MakeChunks(string text) => new[]
    {
        new SearchResultItem { SourcePath = "doc.txt", FileName = "doc.txt", ChunkIndex = 0, ChunkText = text, Score = 1f }
    };
}
