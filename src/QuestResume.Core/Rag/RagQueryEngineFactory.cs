using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Notifications;
using QuestResume.Core.Persistence;

namespace QuestResume.Core.Rag;

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
    int MaxAuditLogLines,
    bool LlmFallbackEnabled,
    bool FaithfulnessCheckEnabled,
    double MinRelevanceThreshold,
    double LlmTemperature,
    double LlmTopP,
    int? LlmSeed,
    string CustomSystemPrompt,
    string SummarizationModelPath)
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
        options.MaxAuditLogLines,
        options.LlmFallbackEnabled,
        options.FaithfulnessCheckEnabled,
        options.MinRelevanceThreshold,
        options.LlmTemperature,
        options.LlmTopP,
        options.LlmSeed,
        options.CustomSystemPrompt,
        options.SummarizationModelPath);
}

public static class RagQueryEngineFactory
{
    public static RagQueryEngine Create(AppOptions options, int? topK = null, HttpClient? httpClient = null)
    {
        ISearchService search = new SearchService(options.IndexPath);
        var providerKind = Enum.TryParse<LlmProviderKind>(options.LlmProvider, ignoreCase: true, out var kind)
            ? kind
            : LlmProviderKind.LlamaSharp;

        var sampling = new LlmSamplingOptions(options.LlmTemperature, options.LlmTopP, options.LlmSeed);
        var personaStore = new PromptPersonaStore(options.IndexPath);

        IVectorStore? vectorStore = null;
        IEmbeddingService? embeddingService = null;
        if (options.EmbeddingsEnabled)
        {
            vectorStore = new VectorStore(options.IndexPath, options.MaxVectorCacheSize, options.VectorQuantizationEnabled, options.AnnSearchEnabled);
            
            embeddingService = new CachingEmbeddingService(
                new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath));
        }

        ICrossEncoderService? crossEncoderService = null;
        if (options.RerankingEnabled)
        {
            crossEncoderService = new CrossEncoderService(options.RerankingModelPath, options.RerankingTokenizerPath);
        }

        
        
        
        IAuditLogRepository auditLog = new AuditLogRepository(options.IndexPath);

        
        
        
        var webhookNotifier = new WebhookNotifier(options.IndexPath, httpClient);

        
        
        
        ILlmProvider? llmProviderOverride = null;
        if (options.LlmFallbackEnabled && IsLlamaSharpConfigured(options.ModelPath) && IsOllamaConfigured(options))
        {
            llmProviderOverride = new RoutingLlmProvider(new ILlmProvider[]
            {
                new OllamaLlmProvider(options.OllamaBaseUrl, options.OllamaModel, httpClient, sampling),
                new LlamaSharpLlmProvider(options.ModelPath, options.ContextSize, options.GpuLayerCount, sampling)
            });
        }

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
            maxAuditLogLines: options.MaxAuditLogLines,
            llmProviderOverride: llmProviderOverride,
            webhookNotifier: webhookNotifier,
            rankFusionStrategy: options.RankFusionStrategy,
            rrfK: options.RrfK,
            queryExpansionEnabled: options.QueryExpansionEnabled,
            hydeEnabled: options.HydeEnabled,
            multiQueryEnabled: options.MultiQueryEnabled,
            multiQueryVariations: options.MultiQueryVariations,
            faithfulnessCheckEnabled: options.FaithfulnessCheckEnabled,
            minRelevanceThreshold: options.MinRelevanceThreshold,
            sampling: sampling,
            customSystemPrompt: options.CustomSystemPrompt,
            personaStore: personaStore,
            summarizationModelPath: options.SummarizationModelPath);
    }

    private static bool IsLlamaSharpConfigured(string modelPath) =>
        !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);

    private static bool IsOllamaConfigured(AppOptions options) =>
        !string.IsNullOrWhiteSpace(options.OllamaBaseUrl) && !string.IsNullOrWhiteSpace(options.OllamaModel);
}
