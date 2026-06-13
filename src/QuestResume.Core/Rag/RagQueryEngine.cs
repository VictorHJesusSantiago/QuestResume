using System.Text;
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
    private readonly VectorStore? _vectorStore;
    private readonly EmbeddingService? _embeddingService;

    private ILlmProvider? _llm;

    public RagQueryEngine(
        SearchService searchService,
        string modelPath,
        int contextSize = 4096,
        int defaultTopK = 5,
        LlmProviderKind llmProvider = LlmProviderKind.LlamaSharp,
        string ollamaBaseUrl = "http://localhost:11434",
        string ollamaModel = "llama3.2",
        HttpClient? httpClient = null,
        VectorStore? vectorStore = null,
        EmbeddingService? embeddingService = null,
        double hybridBm25Weight = 0.5)
    {
        _searchService = new HybridSearchService(searchService, vectorStore, embeddingService, hybridBm25Weight);
        _modelPath = modelPath;
        _contextSize = contextSize;
        _defaultTopK = defaultTopK;
        _llmProviderKind = llmProvider;
        _ollamaBaseUrl = ollamaBaseUrl;
        _ollamaModel = ollamaModel;
        _httpClient = httpClient;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
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
    public async Task<AskResult> AskAsync(string question, int? topK = null, CancellationToken cancellationToken = default)
    {
        var sources = await _searchService.SearchAsync(question, topK ?? _defaultTopK, cancellationToken).ConfigureAwait(false);

        _llm ??= CreateProvider();

        var prompt = BuildPrompt(question, sources);
        var answer = await _llm.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new AskResult { Answer = answer, Sources = sources };
    }

    private ILlmProvider CreateProvider() => _llmProviderKind switch
    {
        LlmProviderKind.Ollama => new OllamaLlmProvider(_ollamaBaseUrl, _ollamaModel, _httpClient),
        _ => new LlamaSharpLlmProvider(_modelPath, _contextSize)
    };

    private static string BuildPrompt(string question, IReadOnlyList<SearchResultItem> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Você é um assistente que responde perguntas com base apenas no contexto abaixo,");
        builder.AppendLine("extraído de arquivos locais do usuário. Se a resposta não estiver no contexto,");
        builder.AppendLine("diga claramente que não encontrou essa informação nos documentos.");
        builder.AppendLine("Sempre cite o nome do arquivo de onde veio a informação usada na resposta.");
        builder.AppendLine();

        if (sources.Count == 0)
        {
            builder.AppendLine("Contexto: (nenhum trecho relevante foi encontrado no índice)");
        }
        else
        {
            for (var i = 0; i < sources.Count; i++)
            {
                builder.AppendLine($"[Trecho {i + 1} - arquivo: {sources[i].FileName}]");
                builder.AppendLine(sources[i].ChunkText);
                builder.AppendLine();
            }
        }

        builder.AppendLine($"Pergunta: {question}");
        builder.AppendLine("Resposta:");

        return builder.ToString();
    }

    public void Dispose()
    {
        _llm?.Dispose();
        _embeddingService?.Dispose();
        _vectorStore?.Dispose();
    }
}
