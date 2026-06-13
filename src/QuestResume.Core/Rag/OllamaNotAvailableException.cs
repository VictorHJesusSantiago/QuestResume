namespace QuestResume.Core.Rag;

/// <summary>
/// Thrown when the Ollama LLM provider is selected but the local Ollama server could not be
/// reached. Full-text search keeps working without an LLM; this exception only affects the
/// "ask a question in natural language" flow.
/// </summary>
public sealed class OllamaNotAvailableException : Exception
{
    public OllamaNotAvailableException(string baseUrl, Exception? innerException = null)
        : base(BuildMessage(baseUrl), innerException)
    {
    }

    private static string BuildMessage(string baseUrl) =>
        $"Não foi possível conectar ao servidor Ollama em '{baseUrl}'. Instale o Ollama " +
        "(https://ollama.com), baixe um modelo (ex.: 'ollama pull llama3.2') e rode " +
        "'ollama serve' (ou deixe o app do Ollama em execução) antes de perguntar.";
}
