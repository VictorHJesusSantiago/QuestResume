namespace QuestResume.Core.Rag;

public sealed class SummarizationService
{
        private const int MaxInputChars = 4000;

    private readonly ILlmProvider _llmProvider;

    public SummarizationService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

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
