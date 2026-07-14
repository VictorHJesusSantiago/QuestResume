using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

/// <summary>
/// Minimal <see cref="ILlmProvider"/> stub for tests that exercise Core.Rag services
/// (StructuredExtractionService, FlashcardService, TranslationService, RoutingLlmProvider)
/// without loading a real GGUF model or hitting a real Ollama server.
/// </summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Func<string, string>? _completeFunc;
    private readonly Exception? _completeException;
    private readonly IReadOnlyList<string>? _streamTokens;
    private readonly Exception? _streamException;
    private readonly int _failAfterTokens;

    public int CompleteCallCount { get; private set; }
    public bool Disposed { get; private set; }

    public FakeLlmProvider(Func<string, string> completeFunc)
    {
        _completeFunc = completeFunc;
    }

    public FakeLlmProvider(Exception completeException)
    {
        _completeException = completeException;
    }

    private FakeLlmProvider(IReadOnlyList<string>? streamTokens, Exception? streamException, int failAfterTokens)
    {
        _streamTokens = streamTokens;
        _streamException = streamException;
        _failAfterTokens = failAfterTokens;
    }

    public static FakeLlmProvider ForStream(IReadOnlyList<string> tokens) => new(tokens, null, int.MaxValue);

    public static FakeLlmProvider ForStreamFailure(Exception exception, int failAfterTokens = 0) =>
        new(null, exception, failAfterTokens);

    public static FakeLlmProvider ForStreamPartialFailure(IReadOnlyList<string> tokens, Exception exception, int failAfterTokens) =>
        new(tokens, exception, failAfterTokens);

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        CompleteCallCount++;
        if (_completeException is not null) throw _completeException;
        return Task.FromResult(_completeFunc!(prompt));
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CompleteCallCount++;
        var emitted = 0;
        if (_streamTokens is not null)
        {
            foreach (var token in _streamTokens)
            {
                if (_streamException is not null && emitted >= _failAfterTokens)
                {
                    throw _streamException;
                }
                await Task.Yield();
                yield return token;
                emitted++;
            }
            yield break;
        }

        if (_streamException is not null && emitted >= _failAfterTokens)
        {
            throw _streamException;
        }

        await Task.Yield();
        yield break;
    }

    public void Dispose() => Disposed = true;
}
