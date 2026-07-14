namespace QuestResume.Core.Models;

/// <summary>
/// The answer produced by the RAG pipeline, together with the chunks used as context.
/// </summary>
public sealed class AskResult
{
    public required string Answer { get; init; }

    public required IReadOnlyList<SearchResultItem> Sources { get; init; }

    /// <summary>
    /// Até 3 sugestões de perguntas relacionadas, geradas best-effort pelo LLM após a resposta
    /// principal. Vazia se a geração falhar ou não houver LLM disponível — nunca faz
    /// <see cref="Rag.RagQueryEngine.AskAsync"/> lançar.
    /// </summary>
    public IReadOnlyList<string> RelatedQuestions { get; init; } = Array.Empty<string>();
}
