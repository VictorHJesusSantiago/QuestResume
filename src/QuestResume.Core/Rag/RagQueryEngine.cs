using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Notifications;
using QuestResume.Core.Persistence;

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
    private readonly IAuditLogRepository? _auditLog;
    private readonly int _gpuLayerCount;
    private readonly int _llmTimeoutSeconds;
    private readonly int _maxAuditLogLines;
    private readonly WebhookNotifier? _webhookNotifier;
    private readonly bool _faithfulnessCheckEnabled;
    private readonly double _minRelevanceThreshold;

    /// <summary>Resposta padrão em PT-BR devolvida pelo guardrail de "não sei" (<see cref="_minRelevanceThreshold"/>).</summary>
    public const string InsufficientContextAnswer =
        "Não encontrei informação suficiente nos documentos indexados para responder com confiança a essa pergunta.";

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

    /// <summary>
    /// Quando informado (via <see cref="RagQueryEngineFactory"/>, ex.: <see cref="RoutingLlmProvider"/>
    /// quando <see cref="Configuration.AppOptions.LlmFallbackEnabled"/> está ativo), este provedor
    /// é usado no lugar de construir um a partir de <see cref="_llmProviderKind"/>.
    /// </summary>
    private readonly ILlmProvider? _llmProviderOverride;

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
        IAuditLogRepository? auditLog = null,
        int gpuLayerCount = 0,
        int llmTimeoutSeconds = 120,
        int maxAuditLogLines = 0,
        ILlmProvider? llmProviderOverride = null,
        WebhookNotifier? webhookNotifier = null,
        string rankFusionStrategy = "Linear",
        int rrfK = 60,
        bool queryExpansionEnabled = false,
        bool hydeEnabled = false,
        bool multiQueryEnabled = false,
        int multiQueryVariations = 3,
        bool faithfulnessCheckEnabled = false,
        double minRelevanceThreshold = 0)
    {
        _webhookNotifier = webhookNotifier;
        _faithfulnessCheckEnabled = faithfulnessCheckEnabled;
        _minRelevanceThreshold = minRelevanceThreshold;
        _llmProviderOverride = llmProviderOverride;
        _searchService = new HybridSearchService(
            searchService, vectorStore, embeddingService, hybridBm25Weight, crossEncoderService, rankFusionStrategy, rrfK,
            llmFactory: GetOrCreateLlmAsync,
            queryExpansionEnabled: queryExpansionEnabled,
            hydeEnabled: hydeEnabled,
            multiQueryEnabled: multiQueryEnabled,
            multiQueryVariations: multiQueryVariations);
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
        _auditLog = auditLog;
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

        // Guardrail de "não sei" (AppOptions.MinRelevanceThreshold): checado ANTES de chamar o
        // LLM para economizar a chamada quando os trechos recuperados claramente não são
        // relevantes o suficiente para sustentar uma resposta.
        if (_minRelevanceThreshold > 0 && RelevanceScoring.AverageNormalizedScore(sources) < _minRelevanceThreshold)
        {
            stopwatch.Stop();

            var guardrailResult = new AskResult
            {
                Answer = InsufficientContextAnswer,
                Sources = sources,
                ConfidenceScore = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful: null)
            };

            if (cacheKey is not null)
            {
                _answerCache[cacheKey] = guardrailResult;
            }

            return guardrailResult;
        }

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildPrompt(question, sources, history);

        using var cts = _llmTimeoutSeconds > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (cts is not null) cts.CancelAfter(TimeSpan.FromSeconds(_llmTimeoutSeconds));
        var effectiveCt = cts?.Token ?? cancellationToken;

        var answer = await llm.CompleteAsync(prompt, effectiveCt).ConfigureAwait(false);

        stopwatch.Stop();

        var relatedQuestions = await TryGenerateRelatedQuestionsAsync(llm, question, answer, cancellationToken).ConfigureAwait(false);

        bool? isFaithful = _faithfulnessCheckEnabled
            ? await TryCheckFaithfulnessAsync(llm, sources, answer, cancellationToken).ConfigureAwait(false)
            : null;

        var result = new AskResult
        {
            Answer = answer,
            Sources = sources,
            RelatedQuestions = relatedQuestions,
            IsFaithful = isFaithful,
            ConfidenceScore = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful)
        };

        if (cacheKey is not null)
        {
            _answerCache[cacheKey] = result;
        }

        if (_auditLog is not null)
        {
            _auditLog.Append(new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Question = question,
                Sources = sources.Select(s => s.FileName).Distinct().ToList(),
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            });
            _auditLog.Rotate(_maxAuditLogLines);
        }

        _webhookNotifier?.Notify("question.asked", new
        {
            question,
            answerLength = answer.Length,
            sources = sources.Select(s => s.FileName).Distinct().ToList(),
            elapsedMs = stopwatch.Elapsed.TotalMilliseconds
        });

        return result;
    }

    /// <summary>
    /// Pede ao LLM 3 perguntas relacionadas curtas à pergunta/resposta recém-geradas, em formato
    /// de lista simples (uma por linha), com parse defensivo linha a linha. Best-effort: qualquer
    /// falha (LLM indisponível, timeout, resposta malformada) é engolida e resulta em lista vazia
    /// — nunca deve comprometer a resposta principal já obtida.
    /// </summary>
    private async Task<IReadOnlyList<string>> TryGenerateRelatedQuestionsAsync(
        ILlmProvider llm, string question, string answer, CancellationToken cancellationToken)
    {
        try
        {
            var prompt =
                "Com base na pergunta e resposta abaixo, sugira exatamente 3 perguntas relacionadas " +
                "curtas que o usuário poderia querer fazer em seguida. Responda apenas com uma lista, " +
                "uma pergunta por linha, sem numeração, sem markdown e sem texto adicional.\n\n" +
                $"Pergunta: {question}\n" +
                $"Resposta: {answer}\n\n" +
                "Perguntas relacionadas:";

            using var cts = _llmTimeoutSeconds > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (cts is not null) cts.CancelAfter(TimeSpan.FromSeconds(_llmTimeoutSeconds));

            var response = await llm.CompleteAsync(prompt, cts?.Token ?? cancellationToken).ConfigureAwait(false);

            return response
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.TrimStart('-', '*', '•', ' ').Trim())
                .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[\.\)]\s*", ""))
                .Where(line => line.Length > 0)
                .Take(3)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
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

        if (_minRelevanceThreshold > 0 && RelevanceScoring.AverageNormalizedScore(sources) < _minRelevanceThreshold)
        {
            var guardrailResult = new AskResult
            {
                Answer = InsufficientContextAnswer,
                Sources = sources,
                ConfidenceScore = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful: null)
            };

            if (cacheKey is not null)
            {
                _answerCache[cacheKey] = guardrailResult;
            }

            return new StreamingAskResult { Sources = sources, Tokens = SingleTokenStream(InsufficientContextAnswer) };
        }

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

        bool? isFaithful = _faithfulnessCheckEnabled
            ? await TryCheckFaithfulnessAsync(llm, sources, answer, cancellationToken).ConfigureAwait(false)
            : null;

        var result = new AskResult
        {
            Answer = answer,
            Sources = sources,
            IsFaithful = isFaithful,
            ConfidenceScore = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful)
        };

        if (cacheKey is not null)
        {
            _answerCache[cacheKey] = result;
        }

        if (_auditLog is not null)
        {
            _auditLog.Append(new AuditLogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                Question = question,
                Sources = sources.Select(s => s.FileName).Distinct().ToList(),
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            });
            _auditLog.Rotate(_maxAuditLogLines);
        }

        _webhookNotifier?.Notify("question.asked", new
        {
            question,
            answerLength = answer.Length,
            sources = sources.Select(s => s.FileName).Distinct().ToList(),
            elapsedMs = stopwatch.Elapsed.TotalMilliseconds
        });
    }

    /// <summary>
    /// Pede ao LLM uma segunda avaliação curta ("responda apenas SIM ou NÃO") sobre se
    /// <paramref name="answer"/> é sustentada pelos trechos recuperados, para
    /// <see cref="Configuration.AppOptions.FaithfulnessCheckEnabled"/>. Parse defensivo: procura
    /// os tokens "sim"/"não" (ou "nao") na resposta, priorizando o que aparece primeiro. Nunca
    /// lança — qualquer falha (LLM indisponível, timeout, resposta ambígua) resulta em
    /// <c>null</c>, sem comprometer a resposta principal já obtida.
    /// </summary>
    private async Task<bool?> TryCheckFaithfulnessAsync(
        ILlmProvider llm, IReadOnlyList<SearchResultItem> sources, string answer, CancellationToken cancellationToken)
    {
        try
        {
            if (sources.Count == 0)
            {
                return false;
            }

            var context = string.Join("\n\n", sources.Select(s => s.ChunkText));
            var prompt =
                "Você é um verificador de fatos. Abaixo estão trechos de documentos e uma resposta gerada a partir deles. " +
                "Responda apenas com uma única palavra: SIM se a resposta é sustentada pelos trechos, ou NÃO caso contrário.\n\n" +
                $"Trechos:\n{context}\n\n" +
                $"Resposta:\n{answer}\n\n" +
                "A resposta é sustentada pelos trechos? (SIM ou NÃO):";

            using var cts = _llmTimeoutSeconds > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            if (cts is not null) cts.CancelAfter(TimeSpan.FromSeconds(_llmTimeoutSeconds));

            var response = await llm.CompleteAsync(prompt, cts?.Token ?? cancellationToken).ConfigureAwait(false);

            return ParseFaithfulnessResponse(response);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Defensive SIM/NÃO parser for <see cref="TryCheckFaithfulnessAsync"/>: takes whichever of
    /// the two tokens appears first in the (possibly verbose, despite the prompt) LLM response.
    /// Returns <c>null</c> when neither token is found.
    /// </summary>
    internal static bool? ParseFaithfulnessResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var normalized = response.ToLowerInvariant();
        var yesIndex = normalized.IndexOf("sim", StringComparison.Ordinal);
        var noIndexA = normalized.IndexOf("não", StringComparison.Ordinal);
        var noIndexB = normalized.IndexOf("nao", StringComparison.Ordinal);
        var noIndex = noIndexA >= 0 && noIndexB >= 0 ? Math.Min(noIndexA, noIndexB) : Math.Max(noIndexA, noIndexB);

        if (yesIndex < 0 && noIndex < 0)
        {
            return null;
        }

        if (noIndex < 0)
        {
            return true;
        }

        if (yesIndex < 0)
        {
            return false;
        }

        return yesIndex < noIndex;
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

    private ILlmProvider CreateProvider()
    {
        if (_llmProviderOverride is not null)
        {
            return _llmProviderOverride;
        }

        return _llmProviderKind switch
        {
            LlmProviderKind.Ollama => new OllamaLlmProvider(_ollamaBaseUrl, _ollamaModel, _httpClient),
            _ => new LlamaSharpLlmProvider(_modelPath, _contextSize, _gpuLayerCount)
        };
    }

    /// <summary>
    /// Exposto para os serviços auxiliares (<see cref="StructuredExtractionService"/>,
    /// <see cref="FlashcardService"/>, <see cref="TranslationService"/>) que precisam do mesmo
    /// serviço de busca e provedor de LLM já configurados neste engine, sem duplicar a lógica de
    /// construção de <see cref="RagQueryEngineFactory"/>.
    /// </summary>
    public HybridSearchService SearchService => _searchService;

    /// <summary>Obtém (inicializando se necessário) o provedor de LLM deste engine.</summary>
    public Task<ILlmProvider> GetLlmProviderAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateLlmAsync(cancellationToken);


    public void Dispose()
    {
        _llm?.Dispose();
        _embeddingService?.Dispose();
        _crossEncoderService?.Dispose();
        _vectorStore?.Dispose();
        _llmInitLock.Dispose();
    }
}
