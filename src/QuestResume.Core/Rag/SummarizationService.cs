namespace QuestResume.Core.Rag;

/// <summary>
/// Gera um resumo curto (2-4 frases, em PT-BR) de um documento usando o LLM configurado,
/// a partir apenas dos primeiros caracteres do texto extraído (não do documento inteiro),
/// mantendo a chamada rápida mesmo para arquivos grandes. Usado por
/// <see cref="Indexing.DocumentIndexer.IndexFolderAsync"/> como etapa separada após a indexação
/// principal (ver <see cref="Configuration.AppOptions.AutoSummarizationEnabled"/>), para que uma
/// falha do LLM durante a sumarização nunca comprometa a indexação em si.
/// </summary>
public sealed class SummarizationService
{
    /// <summary>Número máximo de caracteres do documento enviados ao LLM para sumarização.</summary>
    private const int MaxInputChars = 4000;

    private readonly ILlmProvider _llmProvider;

    public SummarizationService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

    /// <summary>
    /// Retorna um resumo de 2-4 frases em português para <paramref name="text"/>. Lança se o LLM
    /// falhar — o chamador (<see cref="Indexing.DocumentIndexer"/>) é responsável por capturar a
    /// exceção por documento e continuar com os demais.
    /// </summary>
    public async Task<string> SummarizeAsync(string fileName, string text, CancellationToken cancellationToken = default)
    {
        var truncated = text.Length > MaxInputChars ? text[..MaxInputChars] : text;

        var prompt =
            "Resuma o documento abaixo em português, entre 2 e 4 frases objetivas, capturando o " +
            "assunto principal. Responda apenas com o resumo, sem introduções nem markdown.\n\n" +
            $"Documento: {fileName}\n" +
            "<documento>\n" +
            PromptBuilder.SanitizeForPromptInjection(truncated) +
            "\n</documento>\n\nResumo:";

        var response = await _llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        return response.Trim();
    }
}
