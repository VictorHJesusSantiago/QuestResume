namespace QuestResume.Core.Rag;

/// <summary>
/// LLM-backed query/document enhancement helpers used by the "search &amp; RAG quality" opt-in
/// features: query expansion (<see cref="ExpandQueryAsync"/>), HyDE (<see cref="GenerateHypotheticalAnswerAsync"/>),
/// multi-query retrieval (<see cref="GenerateQueryVariationsAsync"/>) and contextual retrieval
/// (<see cref="GenerateShortDocumentContextAsync"/>).
///
/// Every method here follows the same best-effort contract used elsewhere in the codebase (e.g.
/// <see cref="RagQueryEngine.TryGenerateRelatedQuestionsAsync"/>, <see cref="SummarizationService"/>):
/// prompts are short, responses are parsed defensively line-by-line, and callers are expected to
/// wrap calls in try/catch so an LLM failure (timeout, malformed output, model unavailable) never
/// breaks the underlying search/indexing operation — it should just fall back to the
/// non-enhanced behaviour.
/// </summary>
public sealed class QueryEnhancementService
{
    private readonly ILlmProvider _llmProvider;

    public QueryEnhancementService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    /// <summary>
    /// Gera 2-3 sinônimos/termos relacionados a <paramref name="question"/>, um por linha, sem
    /// numeração/markdown. Usados como termos OU adicionais na consulta Lucene (não substituem os
    /// termos originais) — ver <see cref="Indexing.HybridSearchService"/>.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExpandQueryAsync(string question, CancellationToken cancellationToken = default)
    {
        var prompt =
            "Sugira de 2 a 3 sinônimos ou termos relacionados à consulta de busca abaixo, que " +
            "ajudariam a encontrar documentos relevantes mesmo que usem palavras diferentes. " +
            "Responda apenas com uma lista, um termo por linha, sem numeração, sem markdown e sem " +
            "texto adicional.\n\n" +
            $"Consulta: {question}\n\nTermos relacionados:";

        var response = await _llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        return ParseLines(response, maxLines: 3);
    }

    /// <summary>
    /// HyDE: gera uma resposta hipotética curta (1 parágrafo) para <paramref name="question"/>,
    /// como se fosse um trecho de documento respondendo a pergunta. O embedding dessa resposta
    /// hipotética costuma se aproximar mais, no espaço vetorial, dos chunks reais relevantes do
    /// que o embedding da pergunta crua (perguntas e respostas tendem a ter formas linguísticas
    /// diferentes).
    /// </summary>
    public async Task<string?> GenerateHypotheticalAnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        var prompt =
            "Escreva um parágrafo curto (2-4 frases) que poderia ser a resposta à pergunta " +
            "abaixo, como se fosse um trecho de um documento real. Responda apenas com o " +
            "parágrafo, sem introduções nem markdown.\n\n" +
            $"Pergunta: {question}\n\nResposta hipotética:";

        var response = await _llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        var trimmed = response.Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }

    /// <summary>
    /// Gera até <paramref name="count"/> reformulações da pergunta original, cada uma buscando o
    /// mesmo objetivo por um ângulo/vocabulário diferente. A pergunta original é sempre incluída
    /// pelo chamador (ver <see cref="Indexing.HybridSearchService"/>) — este método só retorna as
    /// variações adicionais.
    /// </summary>
    public async Task<IReadOnlyList<string>> GenerateQueryVariationsAsync(string question, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0) return Array.Empty<string>();

        var prompt =
            $"Reformule a pergunta abaixo de {count} maneiras diferentes, mantendo o mesmo " +
            "objetivo/intenção, mas variando vocabulário e ângulo, para ajudar numa busca por " +
            "palavras-chave. Responda apenas com uma lista, uma reformulação por linha, sem " +
            "numeração, sem markdown e sem texto adicional.\n\n" +
            $"Pergunta original: {question}\n\nReformulações:";

        var response = await _llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        return ParseLines(response, maxLines: count);
    }

    /// <summary>
    /// Contextual retrieval: gera um resumo curtíssimo (1-2 frases) de <paramref name="text"/>
    /// (documento inteiro, truncado para caber no prompt) para ser prefixado ao texto de cada
    /// chunk antes de gerar o embedding — ajuda a recuperar chunks que fazem sentido isolados só
    /// com o contexto do documento (ex.: "este trecho é do relatório financeiro de 2023...").
    /// </summary>
    public async Task<string?> GenerateShortDocumentContextAsync(string fileName, string text, CancellationToken cancellationToken = default)
    {
        const int maxInputChars = 4000;
        var truncated = text.Length > maxInputChars ? text[..maxInputChars] : text;

        var prompt =
            "Resuma o documento abaixo em português, em no máximo 2 frases curtas, capturando " +
            "apenas o essencial (tipo de documento e assunto principal) para dar contexto a " +
            "trechos isolados dele. Responda apenas com o resumo, sem introduções nem markdown.\n\n" +
            $"Documento: {fileName}\n" +
            "<documento>\n" +
            PromptBuilder.SanitizeForPromptInjection(truncated) +
            "\n</documento>\n\nContexto:";

        var response = await _llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        var trimmed = response.Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }

    /// <summary>Splits an LLM list response into up to <paramref name="maxLines"/> clean, non-empty lines, stripping bullets/numbering.</summary>
    private static IReadOnlyList<string> ParseLines(string response, int maxLines)
    {
        return response
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '*', '•', ' ').Trim())
            .Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"^\d+[\.\)]\s*", ""))
            .Where(line => line.Length > 0)
            .Take(maxLines)
            .ToList();
    }
}
