using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class RoutingLlmProviderTests
{
    [Fact]
    public async Task CompleteAsync_PrimarySucceeds_DoesNotCallFallback()
    {
        var primary = new FakeLlmProvider(_ => "resposta primária");
        var fallback = new FakeLlmProvider(_ => throw new InvalidOperationException("não deveria ser chamado"));
        using var routing = new RoutingLlmProvider(new ILlmProvider[] { primary, fallback });

        var result = await routing.CompleteAsync("pergunta");

        Assert.Equal("resposta primária", result);
        Assert.Equal(0, fallback.CompleteCallCount);
    }

    [Fact]
    public async Task CompleteAsync_PrimaryFails_FallsBackToSecond()
    {
        var primary = new FakeLlmProvider(new OllamaNotAvailableException("http://localhost:11434"));
        var fallback = new FakeLlmProvider(_ => "resposta de reserva");
        using var routing = new RoutingLlmProvider(new ILlmProvider[] { primary, fallback });

        var result = await routing.CompleteAsync("pergunta");

        Assert.Equal("resposta de reserva", result);
    }

    [Fact]
    public async Task CompleteAsync_AllProvidersFail_ThrowsAggregateWithCombinedMessage()
    {
        var primary = new FakeLlmProvider(new OllamaNotAvailableException("http://localhost:11434"));
        var fallback = new FakeLlmProvider(new ModelNotConfiguredException(string.Empty));
        using var routing = new RoutingLlmProvider(new ILlmProvider[] { primary, fallback });

        var ex = await Assert.ThrowsAsync<AggregateException>(() => routing.CompleteAsync("pergunta"));
        Assert.Contains("Todos os provedores de LLM configurados falharam", ex.Message);
    }

    [Fact]
    public async Task CompleteStreamAsync_PrimaryFailsBeforeFirstToken_FallsBackToSecond()
    {
        var primary = FakeLlmProvider.ForStreamFailure(new OllamaNotAvailableException("http://localhost:11434"));
        var fallback = FakeLlmProvider.ForStream(new[] { "olá", " mundo" });
        using var routing = new RoutingLlmProvider(new ILlmProvider[] { primary, fallback });

        var tokens = new List<string>();
        await foreach (var token in routing.CompleteStreamAsync("pergunta"))
        {
            tokens.Add(token);
        }

        Assert.Equal(new[] { "olá", " mundo" }, tokens);
    }

    [Fact]
    public async Task CompleteStreamAsync_PrimaryFailsAfterEmittingTokens_DoesNotFallBack()
    {
        var primary = FakeLlmProvider.ForStreamPartialFailure(
            new[] { "parte", "parcial" },
            new OllamaNotAvailableException("http://localhost:11434"),
            failAfterTokens: 1);
        var fallback = FakeLlmProvider.ForStream(new[] { "nunca chamado" });
        using var routing = new RoutingLlmProvider(new ILlmProvider[] { primary, fallback });

        var tokens = new List<string>();
        await Assert.ThrowsAsync<OllamaNotAvailableException>(async () =>
        {
            await foreach (var token in routing.CompleteStreamAsync("pergunta"))
            {
                tokens.Add(token);
            }
        });

        Assert.Single(tokens);
        Assert.Equal("parte", tokens[0]);
        Assert.Equal(0, fallback.CompleteCallCount);
    }

    [Fact]
    public void Constructor_EmptyProviderList_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RoutingLlmProvider(Array.Empty<ILlmProvider>()));
    }
}
