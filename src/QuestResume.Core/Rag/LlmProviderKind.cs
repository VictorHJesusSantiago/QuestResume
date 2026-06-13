namespace QuestResume.Core.Rag;

/// <summary>
/// Selects which <see cref="ILlmProvider"/> implementation <see cref="RagQueryEngine"/> uses
/// to generate answers.
/// </summary>
public enum LlmProviderKind
{
    /// <summary>Modelo .gguf embutido, executado em CPU via LLamaSharp/llama.cpp.</summary>
    LlamaSharp,

    /// <summary>Servidor Ollama local (ex.: http://localhost:11434).</summary>
    Ollama
}
