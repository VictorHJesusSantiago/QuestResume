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
    private readonly LlmSamplingOptions _sampling;
    private readonly string _customSystemPrompt;
    private readonly Persistence.PromptPersonaStore? _personaStore;
    private readonly string _summarizationModelPath;
    private readonly SemaphoreSlim _auxLlmInitLock = new(1, 1);
    private ILlmProvider? _auxLlm;

        public const string InsufficientContextAnswer =
        "Não encontrei informação suficiente nos documentos indexados para responder com confiança a essa pergunta.";

        private readonly ConcurrentDictionary<string, AskResult> _answerCache = new();

        private readonly SemaphoreSlim _llmInitLock = new(1, 1);

    private ILlmProvider? _llm;

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
        double minRelevanceThreshold = 0,
        LlmSamplingOptions? sampling = null,
        string customSystemPrompt = "",
        Persistence.PromptPersonaStore? personaStore = null,
        string summarizationModelPath = "")
    {
        _webhookNotifier = webhookNotifier;
        _faithfulnessCheckEnabled = faithfulnessCheckEnabled;
        _minRelevanceThreshold = minRelevanceThreshold;
        _sampling = sampling ?? LlmSamplingOptions.Default;
        _customSystemPrompt = customSystemPrompt ?? string.Empty;
        _personaStore = personaStore;
        _summarizationModelPath = summarizationModelPath ?? string.Empty;
        _llmProviderOverride = llmProviderOverride;
        _searchService = new HybridSearchService(
            searchService, vectorStore, embeddingService, hybridBm25Weight, crossEncoderService, rankFusionStrategy, rrfK,
            llmFactory: GetOrCreateAuxLlmAsync,
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

        public async Task<AskResult> AskAsync(string question, int? topK = null, IReadOnlyList<ChatTurn>? history = null, CancellationToken cancellationToken = default, string? personaName = null, string? userId = null, string? username = null)
    {
        var systemPromptOverride = ResolveSystemPrompt(personaName);

        
        
        var useCache = history is null || history.Count == 0;
        var cacheKey = useCache ? BuildCacheKey(question, topK ?? _defaultTopK, systemPromptOverride) : null;

        if (cacheKey is not null && _answerCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();

        var sources = await _searchService.SearchAsync(question, topK ?? _defaultTopK, cancellationToken).ConfigureAwait(false);

        
        
        
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

        var prompt = PromptBuilder.BuildPrompt(question, sources, history, systemPromptOverride);

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
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                UserId = userId,
                Username = username
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

        public async Task<StreamingAskResult> AskStreamAsync(string question, int? topK = null, IReadOnlyList<ChatTurn>? history = null, CancellationToken cancellationToken = default, string? personaName = null, string? userId = null, string? username = null)
    {
        var systemPromptOverride = ResolveSystemPrompt(personaName);
        var useCache = history is null || history.Count == 0;
        var cacheKey = useCache ? BuildCacheKey(question, topK ?? _defaultTopK, systemPromptOverride) : null;

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

        var prompt = PromptBuilder.BuildPrompt(question, sources, history, systemPromptOverride);

        
        
        return new StreamingAskResult
        {
            Sources = sources,
            Tokens = StreamAndRecordAsync(llm, prompt, question, sources, cacheKey, cancellationToken, userId, username)
        };
    }

    private static async IAsyncEnumerable<string> SingleTokenStream(string text)
    {
        await Task.Yield();
        yield return text;
    }

        private async IAsyncEnumerable<string> StreamAndRecordAsync(
        ILlmProvider llm,
        string prompt,
        string question,
        IReadOnlyList<SearchResultItem> sources,
        string? cacheKey,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        string? userId = null,
        string? username = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var builder = new StringBuilder();

        
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
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                UserId = userId,
                Username = username
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

    private static string BuildCacheKey(string question, int topK, string? systemPromptOverride = null) =>
        $"{topK}|{(systemPromptOverride ?? string.Empty).GetHashCode()}|{question.Trim().ToLowerInvariant()}";

        public Task<AskResult> CompareAsync(string pathA, string pathB, string question, CancellationToken cancellationToken = default) =>
        CompareAsync(new[] { pathA, pathB }, question, cancellationToken);

        public async Task<AskResult> CompareAsync(IReadOnlyList<string> paths, string question, CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Count < 2)
            throw new ArgumentException("Informe ao menos dois documentos para comparar.", nameof(paths));

        var chunksPerDoc = paths.Select(p => _searchService.GetChunksByPath(p)).ToList();

        if (chunksPerDoc.All(c => c.Count == 0))
        {
            return new AskResult
            {
                Answer = "Nenhum dos arquivos informados foi encontrado no índice. Verifique os caminhos e indexe a pasta novamente se necessário.",
                Sources = Array.Empty<SearchResultItem>()
            };
        }

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildMultiComparisonPrompt(question, paths, chunksPerDoc);
        var answer = await llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new AskResult { Answer = answer, Sources = chunksPerDoc.SelectMany(c => c).ToList() };
    }

        public async Task<AskResult> SummarizeMultipleAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths is null || paths.Count == 0)
            throw new ArgumentException("Informe ao menos um documento para resumir.", nameof(paths));

        var chunksPerDoc = paths.Select(p => _searchService.GetChunksByPath(p)).ToList();

        if (chunksPerDoc.All(c => c.Count == 0))
        {
            return new AskResult
            {
                Answer = "Nenhum dos arquivos informados foi encontrado no índice. Verifique os caminhos e indexe a pasta novamente se necessário.",
                Sources = Array.Empty<SearchResultItem>()
            };
        }

        var llm = await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);

        var prompt = PromptBuilder.BuildMultiSummaryPrompt(paths, chunksPerDoc);
        var answer = await llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new AskResult { Answer = answer, Sources = chunksPerDoc.SelectMany(c => c).ToList() };
    }

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
            LlmProviderKind.Ollama => new OllamaLlmProvider(_ollamaBaseUrl, _ollamaModel, _httpClient, _sampling),
            _ => new LlamaSharpLlmProvider(_modelPath, _contextSize, _gpuLayerCount, _sampling)
        };
    }

        private string? ResolveSystemPrompt(string? personaName)
    {
        if (!string.IsNullOrWhiteSpace(personaName) && _personaStore is not null)
        {
            var persona = _personaStore.Find(personaName);
            if (persona is not null && !string.IsNullOrWhiteSpace(persona.SystemPrompt))
            {
                return persona.SystemPrompt;
            }
        }

        return string.IsNullOrWhiteSpace(_customSystemPrompt) ? null : _customSystemPrompt;
    }

        public HybridSearchService SearchService => _searchService;

        public Task<ILlmProvider> GetLlmProviderAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateLlmAsync(cancellationToken);

        public Task<ILlmProvider> GetAuxiliaryLlmProviderAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAuxLlmAsync(cancellationToken);

    private async Task<ILlmProvider> GetOrCreateAuxLlmAsync(CancellationToken cancellationToken)
    {
        
        
        if (string.IsNullOrWhiteSpace(_summarizationModelPath)
            || !File.Exists(_summarizationModelPath)
            || _llmProviderKind != LlmProviderKind.LlamaSharp
            || _llmProviderOverride is not null)
        {
            return await GetOrCreateLlmAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_auxLlm is not null)
        {
            return _auxLlm;
        }

        await _auxLlmInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _auxLlm ??= new LlamaSharpLlmProvider(_summarizationModelPath, _contextSize, _gpuLayerCount, _sampling);
        }
        finally
        {
            _auxLlmInitLock.Release();
        }
    }


    public void Dispose()
    {
        _llm?.Dispose();
        if (!ReferenceEquals(_auxLlm, _llm))
        {
            _auxLlm?.Dispose();
        }
        _embeddingService?.Dispose();
        _crossEncoderService?.Dispose();
        _vectorStore?.Dispose();
        _llmInitLock.Dispose();
        _auxLlmInitLock.Dispose();
    }
}
