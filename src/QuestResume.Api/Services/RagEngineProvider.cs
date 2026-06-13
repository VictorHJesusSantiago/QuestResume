using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Api.Services;

/// <summary>
/// Keeps a single <see cref="RagQueryEngine"/> (and the GGUF model it loads) alive across
/// requests, recreating it only when the configured model or index path changes.
/// Loading a multi-gigabyte model on every request would be far too slow.
/// </summary>
public sealed class RagEngineProvider : IDisposable
{
    private readonly object _lock = new();
    private RagQueryEngine? _engine;
    private string? _loadedModelPath;
    private string? _loadedIndexPath;
    private string? _loadedLlmProvider;
    private string? _loadedOllamaBaseUrl;
    private string? _loadedOllamaModel;
    private bool _loadedEmbeddingsEnabled;
    private string? _loadedEmbeddingModelPath;
    private string? _loadedEmbeddingTokenizerPath;
    private double _loadedHybridBm25Weight;

    public RagQueryEngine GetEngine(AppOptions options)
    {
        lock (_lock)
        {
            if (_engine is null
                || _loadedModelPath != options.ModelPath
                || _loadedIndexPath != options.IndexPath
                || _loadedLlmProvider != options.LlmProvider
                || _loadedOllamaBaseUrl != options.OllamaBaseUrl
                || _loadedOllamaModel != options.OllamaModel
                || _loadedEmbeddingsEnabled != options.EmbeddingsEnabled
                || _loadedEmbeddingModelPath != options.EmbeddingModelPath
                || _loadedEmbeddingTokenizerPath != options.EmbeddingTokenizerPath
                || _loadedHybridBm25Weight != options.HybridBm25Weight)
            {
                _engine?.Dispose();

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

                _engine = new RagQueryEngine(
                    search,
                    options.ModelPath,
                    options.ContextSize,
                    options.TopK,
                    providerKind,
                    options.OllamaBaseUrl,
                    options.OllamaModel,
                    vectorStore: vectorStore,
                    embeddingService: embeddingService,
                    hybridBm25Weight: options.HybridBm25Weight);

                _loadedModelPath = options.ModelPath;
                _loadedIndexPath = options.IndexPath;
                _loadedLlmProvider = options.LlmProvider;
                _loadedOllamaBaseUrl = options.OllamaBaseUrl;
                _loadedOllamaModel = options.OllamaModel;
                _loadedEmbeddingsEnabled = options.EmbeddingsEnabled;
                _loadedEmbeddingModelPath = options.EmbeddingModelPath;
                _loadedEmbeddingTokenizerPath = options.EmbeddingTokenizerPath;
                _loadedHybridBm25Weight = options.HybridBm25Weight;
            }

            return _engine;
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
