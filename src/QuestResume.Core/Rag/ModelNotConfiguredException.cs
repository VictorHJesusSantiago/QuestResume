namespace QuestResume.Core.Rag;

/// <summary>
/// Thrown when an answer is requested but no usable .gguf model has been configured.
/// Full-text search keeps working without an LLM; this exception only affects the
/// "ask a question in natural language" flow.
/// </summary>
public sealed class ModelNotConfiguredException : Exception
{
    public ModelNotConfiguredException(string modelPath)
        : base(BuildMessage(modelPath))
    {
    }

    private static string BuildMessage(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return "Nenhum modelo de IA local foi configurado. Defina o caminho de um arquivo " +
                   ".gguf (ex.: Phi-3-mini-4k-instruct ou Llama-3.2-3B-Instruct) nas configurações.";
        }

        return $"O modelo configurado não foi encontrado em '{modelPath}'. Verifique o caminho " +
               "ou baixe um modelo .gguf e atualize as configurações.";
    }
}
