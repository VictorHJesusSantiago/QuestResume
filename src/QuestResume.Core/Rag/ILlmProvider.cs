namespace QuestResume.Core.Rag;

/// <summary>
/// Abstraction over a local LLM backend used to generate the final answer in the RAG flow.
/// Implementations: <see cref="LlamaSharpLlmProvider"/> (embedded, default) and
/// <see cref="OllamaLlmProvider"/> (local Ollama server).
/// </summary>
public interface ILlmProvider : IDisposable
{
    /// <summary>
    /// Generates a single-shot completion for <paramref name="prompt"/>.
    /// </summary>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a completion for <paramref name="prompt"/>, yielding text fragments as they
    /// become available (used by <c>/api/ask/stream</c> for ChatGPT-style streaming responses).
    /// </summary>
    IAsyncEnumerable<string> CompleteStreamAsync(string prompt, CancellationToken cancellationToken = default);
}
