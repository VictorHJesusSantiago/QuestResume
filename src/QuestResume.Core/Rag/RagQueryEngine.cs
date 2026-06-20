using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

/// <summary>
/// Combines full-text retrieval (<see cref="HybridSearchService"/>) with a local LLM
/// (<see cref="LocalLlmService"/>) to answer natural-language questions about the
/// indexed documents.
/// </summary>
public sealed class RagQueryEngine : IDisposable
{
    private readonly HybridSearchService _searchService;
    private readonly string _modelPath;
    private readonly int _contextSize;
    private readonly int _defaultTopK;
    private readonly LlmProviderKind _llmProviderKind;
    private readonly string _ollamaBaseUrl;
    private readonly string _ollamaModel;
    private readonly HttpClient? _httpClient;
    private readonly IVectorStore? _vectorStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly ICrossEncoderService? _crossEncoderService;
    private readonly string? _indexPath;
    private readonly int _gpuLayerCount;
    private readonly int _llmTimeoutSeconds;
    private readonly int _maxAuditLogLines;

    /// <summary>
    /// Caches answers for repeated questions (same question + topK, no conversation history),
    /// keyed by <see cref="BuildCacheKey"/>. Cleared by <see cref="InvalidateVectorCache"/> so
    /// answers don't go stale after a re-index.
    /// </summary>
    private readonly ConcurrentDictionary<string, AskResult> _answerCache = new();

    /// <summary>
    /// Guards the lazy initialization of <see cref="_llm"/>. This engine can be shared (e.g.
    /// via a cached singleton in <c>RagQueryEngineFactory</c>) across concurrent
    /// <see cref="AskAsync"/> calls — without this, two concurrent first requests could both
    /// pass the null-check and construct (and leak) two <see cref="ILlmProvider"/> instances,
    /// each loading the full GGUF model into memory.
    /// </summary>
    private readonly SemaphoreSlim _llmInitLock = new(1, 1);

    private ILlmProvider? _llm;

    public RagQueryEngine(
        ISearchService searchService,
        string modelPath,
        int contextSize = 4096,
        int defaultTopK = 5,
        LlmProviderKind llmProvider = LlmProviderKind.LlamaSharp,
        string ollamaBaseUrl = "http://localhost:11434",
        string ollamaModel = "llama3.2",
        HttpClient? httpClient = null,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null,
        double hybridBm25Weight = 0.5,
        ICrossEncoderService? crossEncoderService = null,
        string? indexPath = null,
        int gpuLayerCount = 0,
        int llmTimeoutSeconds = 120,
        int maxAuditLogLines = 0)
    {
        _searchService = new HybridSearchService(searchService, vectorStore, embeddingService, hybridBm25Weight, crossEncoderService);
        _modelPath = modelPath;
        _contextSize = contextSize;
        _defaultTopK = defaultTopK;
        _llmProviderKind = llmProvider;
        _ollamaBaseUrl = ollamaBaseUrl;
        _ollamaModel = ollamaModel;
        _httpClient = httpClient;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _crossEncoderService = crossEncoderService;
        _indexPath = indexPath;
        _gpuLayerCount = gpuLayerCount;
        _llmTimeoutSeconds = llmTimeoutSeconds;
        _maxAuditLogLines = maxAuditLogLines;
    }

    /// <summary>
    /// Searches the index for chunks relevant to <paramref name="question"/> and asks the
    /// configured LLM provider to answer using only that context.
    /// </summary>
    /// <exception cref="ModelNotConfiguredException">
    /// Thrown if the LLamaSharp provider is selected but no valid .gguf model path is configured.
    /// </exception>
    /// <exception cref="OllamaNotAvailableException">
    /// Thrown if the Ollama provider is selected but the local Ollama server is unreachable.
    /// </exception>
    /// <remarks>
    /// Use <see cref="SearchService"/> directly for keyword search without an LLM.
    /// </remarks>
    public async Task<AskResult> AskAsync(string question, int? topK = null, IReadOnlyList<ChatTurn>? history = null, CancellationToken cancellationToken = default)
    {
        // Only cache standalone questions: a follow-up's correct answer depends on the
        // conversation history, so it can't reuse an answer computed without it.
        var useCache = history is null || history.Count == 0;
        var cacheKey = useCache ? BuildCacheKey(question, topK ?? _defaultTopK) : null;

        if (cacheKey is not null && _answerCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();

        var sources = await _searchService.SearchAsync(question, topK ?? _defaultTopK, cancellationToken).ConfigureAwait(false);

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildPrompt(question, sources, history);

        using var cts = _llmTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (cts is not null) cts.CancelAfter(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        var effectiveCt = cts?.Token ?? cancellationToken;

        var answer = await llm.CompleteAsync(prompt, effectiveCt).ConfigureAwait(false);

        stopwatch.Stop();

        var result = new AskResult { Answer = answer, Sources = sources };

        if (cacheKey is not null)
        {
            _answerCache[cacheKey] = result;
        }

        if (_indexPath is not null)
        {
            AuditLog.Append(_indexPath, new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Question = question,
                Sources = sources.Select(s => s.FileName).Distinct().ToList(),
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            });
            AuditLog.Rotate(_indexPath, _maxAuditLogLines);
        }

        return result;
    }

    /// <summary>
    /// Same retrieval/prompt-building as <see cref="AskAsync"/>, but returns the LLM's answer as
    /// a token stream for ChatGPT-style incremental rendering. Sources are available
    /// immediately (retrieval happens before this method returns); the answer text is cached
    /// and audit-logged once the returned <see cref="StreamingAskResult.Tokens"/> stream is
    /// fully enumerated.
    /// </summary>
    public async Task<StreamingAskResult> AskStreamAsync(string question, int? topK = null, IReadOnlyList<ChatTurn>? history = null, CancellationToken cancellationToken = default)
    {
        var useCache = history is null || history.Count == 0;
        var cacheKey = useCache ? BuildCacheKey(question, topK ?? _defaultTopK) : null;

        if (cacheKey is not null && _answerCache.TryGetValue(cacheKey, out var cached))
        {
            return new StreamingAskResult { Sources = cached.Sources, Tokens = SingleTokenStream(cached.Answer) };
        }

        var sources = await _searchService.SearchAsync(question, topK ?? _defaultTopK, cancellationToken).ConfigureAwait(false);

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildPrompt(question, sources, history);

        // Timeout is enforced inside StreamAndRecordAsync via a linked CTS so the semaphore
        // held by RagEngineProvider is released when the stream times out.
        return new StreamingAskResult
        {
            Sources = sources,
            Tokens = StreamAndRecordAsync(llm, prompt, question, sources, cacheKey, cancellationToken)
        };
    }

    private static async IAsyncEnumerable<string> SingleTokenStream(string text)
    {
        await Task.Yield();
        yield return text;
    }

    /// <summary>
    /// Streams tokens from <paramref name="llm"/> and, once exhausted, caches (if
    /// <paramref name="cacheKey"/> is set) and audit-logs the assembled answer — mirroring the
    /// post-processing <see cref="AskAsync"/> does after a non-streaming completion.
    /// </summary>
    private async IAsyncEnumerable<string> StreamAndRecordAsync(
        ILlmProvider llm,
        string prompt,
        string question,
        IReadOnlyList<SearchResultItem> sources,
        string? cacheKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var builder = new StringBuilder();

        // Apply the same per-inference timeout used in the non-streaming path.
        using var cts = _llmTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (cts is not null) cts.CancelAfter(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        var effectiveCt = cts?.Token ?? cancellationToken;

        await foreach (var token in llm.CompleteStreamAsync(prompt, effectiveCt).ConfigureAwait(false))
        {
            builder.Append(token);
            yield return token;
        }

        stopwatch.Stop();

        var answer = builder.ToString().Trim();
        var result = new AskResult { Answer = answer, Sources = sources };

        if (cacheKey is not null)
        {
            _answerCache[cacheKey] = result;
        }

        if (_indexPath is not null)
        {
            AuditLog.Append(_indexPath, new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Question = question,
                Sources = sources.Select(s => s.FileName).Distinct().ToList(),
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            });
            AuditLog.Rotate(_indexPath, _maxAuditLogLines);
        }
    }

    private static string BuildCacheKey(string question, int topK) =>
        $"{topK}|{question.Trim().ToLowerInvariant()}";

    /// <summary>
    /// Answers <paramref name="question"/> about two specific indexed files, e.g. "what
    /// changed between these two contracts?". Retrieves every indexed chunk for each file
    /// (via <see cref="HybridSearchService.GetChunksByPath"/>) rather than ranking by
    /// relevance, since both documents are explicitly chosen by the caller.
    /// </summary>
    public async Task<AskResult> CompareAsync(string pathA, string pathB, string question, CancellationToken cancellationToken = default)
    {
        var chunksA = _searchService.GetChunksByPath(pathA);
        var chunksB = _searchService.GetChunksByPath(pathB);

        if (chunksA.Count == 0 && chunksB.Count == 0)
        {
            return new AskResult
            {
                Answer = "Nenhum dos dois arquivos informados foi encontrado no índice. Verifique os caminhos e indexe a pasta novamente se necessário.",
                Sources = Array.Empty<SearchResultItem>()
            };
        }

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildComparisonPrompt(question, pathA, pathB, chunksA, chunksB);
        var answer = await llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new AskResult { Answer = answer, Sources = chunksA.Concat(chunksB).ToList() };
    }

    /// <summary>
    /// Drops the vector-search in-memory cache (<see cref="VectorStore.InvalidateCache"/>), so a
    /// subsequent <see cref="AskAsync"/> on this (possibly cached/shared) engine picks up
    /// embeddings written by a re-index that happened through a different
    /// <see cref="VectorStore"/> instance pointed at the same database file.
    /// </summary>
    public void InvalidateVectorCache()
    {
        _vectorStore?.InvalidateCache();
        _answerCache.Clear();
    }

    private async Task<ILlmProvider> GetOrCreateLlmAsync(CancellationToken cancellationToken)
    {
        if (_llm is not null)
        {
            return _llm;
        }

        await _llmInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _llm ??= CreateProvider();
        }
        finally
        {
            _llmInitLock.Release();
        }
    }

    private ILlmProvider CreateProvider() => _llmProviderKind switch
    {
        LlmProviderKind.Ollama => new OllamaLlmProvider(_ollamaBaseUrl, _ollamaModel, _httpClient),
        _ => new LlamaSharpLlmProvider(_modelPath, _contextSize, _gpuLayerCount)
    };


    public void Dispose()
    {
        _llm?.Dispose();
        _embeddingService?.Dispose();
        _crossEncoderService?.Dispose();
        _vectorStore?.Dispose();
        _llmInitLock.Dispose();
    }
}
