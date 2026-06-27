using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Persistence;

namespace QuestResume.Core.Rag;

/// <summary>
/// The subset of <see cref="AppOptions"/> (plus an optional per-request <c>topK</c> override)
/// that determines how a <see cref="RagQueryEngine"/> is constructed. Front-ends that cache an
/// engine instance (API, Desktop) can compare a previously captured key against a freshly
/// loaded one — via record equality — to decide whether the cached engine can be reused or
/// must be rebuilt.
/// </summary>
public sealed record RagEngineKey(
    string ModelPath,
    string IndexPath,
    int ContextSize,
    int TopK,
    string LlmProvider,
    string OllamaBaseUrl,
    string OllamaModel,
    bool EmbeddingsEnabled,
    string EmbeddingModelPath,
    string EmbeddingTokenizerPath,
    double HybridBm25Weight,
    bool RerankingEnabled,
    string RerankingModelPath,
    string RerankingTokenizerPath,
    int GpuLayerCount,
    int LlmTimeoutSeconds,
    int MaxVectorCacheSize,
    int MaxAuditLogLines)
{
    public static RagEngineKey From(AppOptions options, int? topK = null) => new(
        options.ModelPath,
        options.IndexPath,
        options.ContextSize,
        topK ?? options.TopK,
        options.LlmProvider,
        options.OllamaBaseUrl,
        options.OllamaModel,
        options.EmbeddingsEnabled,
        options.EmbeddingModelPath,
        options.EmbeddingTokenizerPath,
        options.HybridBm25Weight,
        options.RerankingEnabled,
        options.RerankingModelPath,
        options.RerankingTokenizerPath,
        options.GpuLayerCount,
        options.LlmTimeoutSeconds,
        options.MaxVectorCacheSize,
        options.MaxAuditLogLines);
}

/// <summary>
/// Builds a <see cref="RagQueryEngine"/> (and its <see cref="SearchService"/>, optional
/// <see cref="VectorStore"/> and <see cref="EmbeddingService"/>) from <see cref="AppOptions"/>.
/// Used by the CLI, API and Desktop front-ends so the wiring lives in one place instead of
/// being copy-pasted three times.
/// </summary>
public static class RagQueryEngineFactory
{
    public static RagQueryEngine Create(AppOptions options, int? topK = null, HttpClient? httpClient = null)
    {
        ISearchService search = new SearchService(options.IndexPath);
        var providerKind = Enum.TryParse<LlmProviderKind>(options.LlmProvider, ignoreCase: true, out var kind)
            ? kind
            : LlmProviderKind.LlamaSharp;

        IVectorStore? vectorStore = null;
        IEmbeddingService? embeddingService = null;
        if (options.EmbeddingsEnabled)
        {
            vectorStore = new VectorStore(options.IndexPath, options.MaxVectorCacheSize);
            embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        }

        ICrossEncoderService? crossEncoderService = null;
        if (options.RerankingEnabled)
        {
            crossEncoderService = new CrossEncoderService(options.RerankingModelPath, options.RerankingTokenizerPath);
        }

        // AuditLogRepository is injected (not newed inside the engine) so tests can substitute
        // IAuditLogRepository without touching the filesystem, and so the interface is actually
        // used as a dependency rather than existing only as dead abstraction.
        IAuditLogRepository auditLog = new AuditLogRepository(options.IndexPath);

        return new RagQueryEngine(
            search,
            options.ModelPath,
            options.ContextSize,
            topK ?? options.TopK,
            providerKind,
            options.OllamaBaseUrl,
            options.OllamaModel,
            httpClient: httpClient,
            vectorStore: vectorStore,
            embeddingService: embeddingService,
            hybridBm25Weight: options.HybridBm25Weight,
            crossEncoderService: crossEncoderService,
            auditLog: auditLog,
            gpuLayerCount: options.GpuLayerCount,
            llmTimeoutSeconds: options.LlmTimeoutSeconds,
            maxAuditLogLines: options.MaxAuditLogLines);
    }
}
