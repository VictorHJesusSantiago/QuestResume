using QuestResume.Core.Indexing;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Executa uma busca explícita no índice já indexado e devolve os trechos mais relevantes
/// (item 11). Útil como ferramenta que o agente pode invocar deliberadamente, além da recuperação
/// RAG automática. Diferente do fluxo RAG normal, aqui o agente decide quando e o que buscar.
/// </summary>
public sealed class IndexQueryTool : ITool
{
    private const int TopK = 5;
    private const int MaxCharsPerChunk = 500;

    private readonly ISearchService _searchService;

    public IndexQueryTool(ISearchService searchService)
    {
        _searchService = searchService;
    }

    public string Name => "index_query";

    public string Description =>
        "Busca trechos relevantes nos documentos já indexados. Passe os termos de busca como " +
        "entrada. Use quando precisar consultar explicitamente o conteúdo indexado do usuário.";

    public Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new IndexQueryToolException("Informe os termos de busca.");
        }

        if (!_searchService.IndexExists())
        {
            return Task.FromResult("O índice ainda não foi criado. Indexe uma pasta de documentos primeiro.");
        }

        var results = _searchService.Search(input.Trim(), TopK);
        if (results.Count == 0)
        {
            return Task.FromResult("Nenhum trecho relevante encontrado no índice para essa busca.");
        }

        var lines = results.Select((r, i) =>
        {
            var text = r.ChunkText.Length > MaxCharsPerChunk ? r.ChunkText[..MaxCharsPerChunk] + "..." : r.ChunkText;
            return $"[{i + 1}] ({r.FileName}) {text}";
        });

        return Task.FromResult(string.Join("\n\n", lines));
    }
}

/// <summary>Lançada quando <see cref="IndexQueryTool"/> recebe entrada inválida.</summary>
public sealed class IndexQueryToolException : Exception
{
    public IndexQueryToolException(string message) : base(message)
    {
    }
}
