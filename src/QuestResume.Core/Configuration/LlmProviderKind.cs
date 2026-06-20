namespace QuestResume.Core.Configuration;

/// <summary>
/// Selects which LLM provider implementation <see cref="QuestResume.Core.Rag.RagQueryEngine"/>
/// uses to generate answers. Lives in the Configuration namespace so
/// <see cref="AppOptions.Validate"/> can reference it without creating a dependency from
/// Configuration onto the Rag layer.
/// </summary>
public enum LlmProviderKind
{
    /// <summary>Modelo .gguf embutido, executado em CPU via LLamaSharp/llama.cpp.</summary>
    LlamaSharp,

    /// <summary>Servidor Ollama local (ex.: http://localhost:11434).</summary>
    Ollama
}
