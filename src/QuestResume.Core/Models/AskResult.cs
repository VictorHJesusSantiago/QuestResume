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

    /// <summary>
    /// Resultado da verificação de fidelidade (<see cref="Configuration.AppOptions.FaithfulnessCheckEnabled"/>):
    /// <c>true</c>/<c>false</c> conforme o LLM avaliou se a resposta é sustentada pelos trechos
    /// fornecidos, ou <c>null</c> quando a checagem está desabilitada ou falhou (best-effort —
    /// nunca faz <see cref="Rag.RagQueryEngine.AskAsync"/> lançar).
    /// </summary>
    public bool? IsFaithful { get; init; }

    /// <summary>
    /// Score heurístico (0-1) de confiança na resposta, combinando a relevância média dos trechos
    /// recuperados com o resultado de <see cref="IsFaithful"/> quando disponível. <c>null</c>
    /// somente para respostas onde nenhum trecho foi recuperado (sem base para o cálculo).
    /// </summary>
    public double? ConfidenceScore { get; init; }
}
