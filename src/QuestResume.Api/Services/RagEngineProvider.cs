using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Obtém o provedor de LLM do engine configurado (item 7 — usado pelo endpoint de benchmark).
    /// Não segura o semáforo de perguntas: o benchmark deve rodar isoladamente por conta do
    /// chamador.
    /// </summary>
    public Task<ILlmProvider> GetLlmProviderAsync(AppOptions options, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        return engine.GetLlmProviderAsync(cancellationToken);
    }

    public async Task<AskResult> AskAsync(AppOptions options, string question, int? topK, IReadOnlyList<ChatTurn>? history, CancellationToken cancellationToken, string? persona = null)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await engine.AskAsync(question, topK, history, cancellationToken, persona).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    /// <summary>
    /// Streaming counterpart to <see cref="AskAsync"/>. Holds <see cref="_askSemaphore"/> for
    /// the lifetime of the returned token stream (released once it's fully enumerated, or if
    /// the caller disconnects) so a concurrent <c>/api/ask</c>/<c>/api/ask/stream</c> request
    /// can't run inference on the shared model at the same time.
    /// </summary>
    /// <summary>
    /// Processes a batch of questions sequentially through <see cref="RagQueryEngine.AskAsync"/>,
    /// one at a time — each call still goes through <see cref="AskAsync"/> and therefore acquires
    /// <see cref="_askSemaphore"/>, so questions never run concurrently against the shared local
    /// LLM. A per-question failure (e.g. the model/Ollama becomes unavailable mid-batch) is
    /// captured in <see cref="BatchAskResultItem.Error"/> instead of aborting the remaining
    /// questions.
    /// </summary>
    public async Task<List<BatchAskResultItem>> AskBatchAsync(AppOptions options, IReadOnlyList<string> questions, int? topK, CancellationToken cancellationToken)
    {
        var results = new List<BatchAskResultItem>(questions.Count);

        foreach (var question in questions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await AskAsync(options, question, topK, history: null, cancellationToken).ConfigureAwait(false);
                results.Add(new BatchAskResultItem { Question = question, Answer = result.Answer, Sources = result.Sources });
            }
            catch (Exception ex) when (ex is ModelNotConfiguredException or OllamaNotAvailableException)
            {
                results.Add(new BatchAskResultItem { Question = question, Answer = string.Empty, Sources = Array.Empty<QuestResume.Core.Models.SearchResultItem>(), Error = ex.Message });
            }
        }

        return results;
    }

    public async Task<StreamingAskResult> AskStreamAsync(AppOptions options, string question, int? topK, IReadOnlyList<ChatTurn>? history, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await engine.AskStreamAsync(question, topK, history, cancellationToken).ConfigureAwait(false);
            return new StreamingAskResult { Sources = result.Sources, Tokens = ReleaseAfter(result.Tokens, cancellationToken) };
        }
        catch
        {
            _askSemaphore.Release();
            throw;
        }
    }

    private async IAsyncEnumerable<string> ReleaseAfter(IAsyncEnumerable<string> tokens, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var token in tokens.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return token;
            }
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<AskResult> CompareAsync(AppOptions options, string pathA, string pathB, string question, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await engine.CompareAsync(pathA, pathB, question, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<AskResult> CompareAsync(AppOptions options, IReadOnlyList<string> paths, string question, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try { return await engine.CompareAsync(paths, question, cancellationToken).ConfigureAwait(false); }
        finally { _askSemaphore.Release(); }
    }

    public async Task<AskResult> SummarizeMultipleAsync(AppOptions options, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try { return await engine.SummarizeMultipleAsync(paths, cancellationToken).ConfigureAwait(false); }
        finally { _askSemaphore.Release(); }
    }

    public async Task<MindMapNode> GenerateMindMapAsync(AppOptions options, string path, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            return await new MindMapService(engine.SearchService.GetChunksByPath, llm).GenerateAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally { _askSemaphore.Release(); }
    }

    public async Task<List<TimelineEvent>> ExtractTimelineAsync(AppOptions options, string path, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            return await new TimelineExtractionService(engine.SearchService.GetChunksByPath, llm).ExtractAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally { _askSemaphore.Release(); }
    }

    public async Task<List<string>> GenerateOutlineAsync(AppOptions options, string path, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            return await new DocumentOutlineService(engine.SearchService.GetChunksByPath, llm).GenerateAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally { _askSemaphore.Release(); }
    }

    public async Task<List<ExtractedEntity>> ExtractEntitiesAsync(AppOptions options, string path, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);
        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            var chunks = engine.SearchService.GetChunksByPath(path);
            if (chunks.Count == 0)
                throw new InvalidOperationException($"Nenhum conteúdo indexado foi encontrado para '{path}'.");
            var text = string.Join("\n\n", chunks.Select(c => c.ChunkText));
            return await new EntityExtractionService(llm).ExtractAsync(text, cancellationToken).ConfigureAwait(false);
        }
        finally { _askSemaphore.Release(); }
    }

    public async Task<TableExtractionResult> ExtractTableAsync(AppOptions options, string path, string? instruction, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            var service = new StructuredExtractionService(engine.SearchService.GetChunksByPath, llm);
            return await service.ExtractTableAsync(path, instruction, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<List<Flashcard>> GenerateFlashcardsAsync(AppOptions options, string path, int count, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
            return await service.GenerateFlashcardsAsync(path, count, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<List<QuizQuestion>> GenerateQuizAsync(AppOptions options, string path, int count, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
            return await service.GenerateQuizAsync(path, count, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    public async Task<string> TranslateAsync(AppOptions options, string text, string targetLanguage, CancellationToken cancellationToken)
    {
        var engine = GetEngine(options);

        await AcquireSemaphoreAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var llm = await engine.GetLlmProviderAsync(cancellationToken).ConfigureAwait(false);
            var service = new TranslationService(llm);
            return await service.TranslateAsync(text, targetLanguage, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _askSemaphore.Release();
        }
    }

    /// <summary>
    /// Acquires <see cref="_askSemaphore"/> with a 30-second timeout so queued requests
    /// are not held indefinitely when the LLM is generating a long response. Returns 503 to
    /// the caller (via the thrown exception) rather than silently blocking forever.
    /// </summary>
    private async Task AcquireSemaphoreAsync(CancellationToken cancellationToken)
    {
        var acquired = await _askSemaphore.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new InvalidOperationException(
                "O servidor está processando outra inferência. Aguarde 30 segundos e tente novamente.");
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
