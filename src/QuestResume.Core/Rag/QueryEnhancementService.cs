namespace QuestResume.Core.Rag;

public sealed class QueryEnhancementService
{
    private readonly ILlmProvider _llmProvider;

    public QueryEnhancementService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

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
