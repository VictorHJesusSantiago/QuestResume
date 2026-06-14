using QuestResume.Core.Configuration;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Api.Services;

/// <summary>
/// Keeps a single <see cref="RagQueryEngine"/> (and the GGUF model it loads) alive across
/// requests, recreating it only when the configuration captured by <see cref="RagEngineKey"/>
/// changes. Loading a multi-gigabyte model on every request would be far too slow.
/// </summary>
public sealed class RagEngineProvider : IDisposable
{
    private readonly object _lock = new();

    /// <summary>
    /// Serializes <see cref="RagQueryEngine.AskAsync"/> calls: a single LLamaSharp
    /// <c>StatelessExecutor</c>/model context is not safe for concurrent inference, so
    /// overlapping <c>/api/ask</c> requests must queue rather than race on the same model.
    /// </summary>
    private readonly SemaphoreSlim _askSemaphore = new(1, 1);

    private RagQueryEngine? _engine;
    private RagEngineKey? _loadedKey;

    public RagQueryEngine GetEngine(AppOptions options)
    {
        lock (_lock)
        {
            var key = RagEngineKey.From(options);
            if (_engine is null || _loadedKey != key)
            {
                _engine?.Dispose();
                _engine = RagQueryEngineFactory.Create(options);
                _loadedKey = key;
            }

            return _engine;
        }
    }

    public async Task<AskResult> AskAsync(AppOptions options, string question, int? topK, IReadOnlyList<ChatTurn>? history, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await _askSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await engine.AskAsync(question, topK, history, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<AskResult> CompareAsync(AppOptions options, string pathA, string pathB, string question, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await _askSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await engine.CompareAsync(pathA, pathB, question, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    /// <summary>
    /// Drops the cached engine's vector-search cache after a re-index, so <c>/api/ask</c>
    /// immediately reflects newly indexed embeddings instead of a stale pre-reindex snapshot.
    /// </summary>
    public void InvalidateVectorCache()
    {
        lock (_lock)
        {
            _engine?.InvalidateVectorCache();
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
        _askSemaphore.Dispose();
    }
}
