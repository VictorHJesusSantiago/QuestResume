using System.Text;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

/// <summary>
/// Combines full-text retrieval (<see cref="SearchService"/>) with a local LLM
/// (<see cref="LocalLlmService"/>) to answer natural-language questions about the
/// indexed documents.
/// </summary>
public sealed class RagQueryEngine : IDisposable
{
    private readonly SearchService _searchService;
    private readonly string _modelPath;
    private readonly int _contextSize;
    private readonly int _defaultTopK;

    private LocalLlmService? _llm;

    public RagQueryEngine(SearchService searchService, string modelPath, int contextSize = 4096, int defaultTopK = 5)
    {
        _searchService = searchService;
        _modelPath = modelPath;
        _contextSize = contextSize;
        _defaultTopK = defaultTopK;
    }

    /// <summary>
    /// Searches the index for chunks relevant to <paramref name="question"/> and asks the
    /// local LLM to answer using only that context.
    /// </summary>
    /// <exception cref="ModelNotConfiguredException">
    /// Thrown if no valid .gguf model path is configured. Use <see cref="SearchService"/>
    /// directly for keyword search without an LLM.
    /// </exception>
    public async Task<AskResult> AskAsync(string question, int? topK = null, CancellationToken cancellationToken = default)
    {
        var sources = _searchService.Search(question, topK ?? _defaultTopK);

        _llm ??= LocalLlmService.Load(_modelPath, _contextSize);

        var prompt = BuildPrompt(question, sources);
        var answer = await _llm.AskAsync(prompt, cancellationToken).ConfigureAwait(false);

        return new AskResult { Answer = answer, Sources = sources };
    }

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
    }
}
