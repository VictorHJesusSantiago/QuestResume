using System.Text;
using LLama;
using LLama.Common;

namespace QuestResume.Core.Rag;

/// <summary>
/// Wraps a local GGUF model loaded via LLamaSharp (llama.cpp), running entirely on CPU
/// in-process. No network access and no per-token cost.
/// </summary>
public sealed class LocalLlmService : IDisposable
{
    private readonly LLamaWeights _weights;
    private readonly ModelParams _modelParams;

    private LocalLlmService(LLamaWeights weights, ModelParams modelParams)
    {
        _weights = weights;
        _modelParams = modelParams;
    }

    /// <summary>
    /// Loads the model at <paramref name="modelPath"/>. Throws <see cref="ModelNotConfiguredException"/>
    /// if the path is empty or the file doesn't exist.
    /// </summary>
    public static LocalLlmService Load(string modelPath, int contextSize = 4096)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new ModelNotConfiguredException(modelPath);
        }

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = (uint)contextSize,
            GpuLayerCount = 0
        };

        var weights = LLamaWeights.LoadFromFile(modelParams);
        return new LocalLlmService(weights, modelParams);
    }

    /// <summary>
    /// Runs a single-shot, stateless completion for <paramref name="prompt"/>.
    /// </summary>
    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var executor = new StatelessExecutor(_weights, _modelParams);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 512,
            AntiPrompts = new List<string> { "PERGUNTA_DO_USUARIO:", "<documento" }
        };

        var builder = new StringBuilder();
        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken).ConfigureAwait(false))
        {
            builder.Append(token);
        }

        return builder.ToString().Trim();
    }

    public void Dispose()
    {
        _weights.Dispose();
    }
}
