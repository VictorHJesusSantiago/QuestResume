namespace QuestResume.Core.Rag;

/// <summary>
/// Lançada quando a resposta do LLM não pôde ser interpretada como o JSON estrito solicitado
/// no prompt (extração de tabela, geração de flashcards/quiz), mesmo após a extração defensiva
/// do bloco JSON de dentro de um possível texto extra.
/// </summary>
public sealed class LlmJsonParseException : Exception
{
    public LlmJsonParseException(string context, string rawResponse, Exception? innerException = null)
        : base(BuildMessage(context, rawResponse), innerException)
    {
    }

    private static string BuildMessage(string context, string rawResponse)
    {
        var preview = rawResponse.Length > 300 ? rawResponse[..300] + "..." : rawResponse;
        return $"Não foi possível interpretar a resposta do modelo como JSON válido ({context}). " +
               $"Tente novamente ou ajuste a instrução. Resposta recebida: \"{preview}\"";
    }
}
