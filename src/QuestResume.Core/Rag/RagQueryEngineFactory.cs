using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;

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
    string RerankingTokenizerPath)
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
        options.RerankingTokenizerPath);
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
        var search = new SearchService(options.IndexPath);
        var providerKind = Enum.TryParse<LlmProviderKind>(options.LlmProvider, ignoreCase: true, out var kind)
            ? kind
            : LlmProviderKind.LlamaSharp;

        VectorStore? vectorStore = null;
        EmbeddingService? embeddingService = null;
        if (options.EmbeddingsEnabled)
        {
            vectorStore = new VectorStore(options.IndexPath);
            embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
        }

        CrossEncoderService? crossEncoderService = null;
        if (options.RerankingEnabled)
        {
            crossEncoderService = new CrossEncoderService(options.RerankingModelPath, options.RerankingTokenizerPath);
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
            crossEncoderService: crossEncoderService);
    }
}
