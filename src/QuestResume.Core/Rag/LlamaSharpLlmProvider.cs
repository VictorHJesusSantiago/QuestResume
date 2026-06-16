namespace QuestResume.Core.Rag;

/// <summary>
/// <see cref="ILlmProvider"/> backed by a local .gguf model running on CPU via LLamaSharp
/// (<see cref="LocalLlmService"/>). This is the default, zero-install provider.
/// </summary>
public sealed class LlamaSharpLlmProvider : ILlmProvider
{
    private readonly LocalLlmService _llm;

    /// <summary>
    /// Loads the model at <paramref name="modelPath"/>. Throws
    /// <see cref="ModelNotConfiguredException"/> if the path is empty or the file doesn't exist.
    /// </summary>
    public LlamaSharpLlmProvider(string modelPath, int contextSize = 4096, int gpuLayerCount = 0)
    {
        _llm = LocalLlmService.Load(modelPath, contextSize, gpuLayerCount);
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        _llm.AskAsync(prompt, cancellationToken);

    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, CancellationToken cancellationToken = default) =>
        _llm.AskStreamAsync(prompt, cancellationToken);

    public void Dispose() => _llm.Dispose();
}
