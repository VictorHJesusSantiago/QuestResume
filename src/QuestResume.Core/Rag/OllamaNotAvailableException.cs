namespace QuestResume.Core.Rag;

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
