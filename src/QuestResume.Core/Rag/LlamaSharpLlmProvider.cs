namespace QuestResume.Core.Rag;

public sealed class LlamaSharpLlmProvider : ILlmProvider
{
    private readonly LocalLlmService _llm;

        public LlamaSharpLlmProvider(string modelPath, int contextSize = 4096, int gpuLayerCount = 0, LlmSamplingOptions? sampling = null)
    {
        _llm = LocalLlmService.Load(modelPath, contextSize, gpuLayerCount, sampling);
    }

    public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
        _llm.AskAsync(prompt, cancellationToken);

    public IAsyncEnumerable<string> CompleteStreamAsync(string prompt, CancellationToken cancellationToken = default) =>
        _llm.AskStreamAsync(prompt, cancellationToken);

    public void Dispose() => _llm.Dispose();
}
