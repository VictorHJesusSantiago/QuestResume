using System.Diagnostics;

namespace QuestResume.Core.Rag;

public sealed class ModelBenchmarkResult
{
        public double TokensPerSecond { get; set; }

        public double TotalTimeMs { get; set; }

        public int TotalTokens { get; set; }

        public int PromptCount { get; set; }
}

public sealed class ModelBenchmarkService
{
    private readonly ILlmProvider _llmProvider;

        public static IReadOnlyList<string> DefaultPrompts { get; } = new[]
    {
        "Explique em um parágrafo o que é indexação de documentos.",
        "Liste três vantagens de rodar um modelo de linguagem localmente.",
        "Escreva uma frase motivacional sobre aprendizado contínuo.",
    };

    public ModelBenchmarkService(ILlmProvider llmProvider)
    {
        _llmProvider = llmProvider;
    }

        public async Task<ModelBenchmarkResult> RunAsync(IReadOnlyList<string>? prompts = null, CancellationToken cancellationToken = default)
    {
        prompts ??= DefaultPrompts;

        var totalTokens = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var prompt in prompts)
        {
            await foreach (var fragment in _llmProvider.CompleteStreamAsync(prompt, cancellationToken).ConfigureAwait(false))
            {
                if (!string.IsNullOrEmpty(fragment))
                {
                    totalTokens++;
                }
            }
        }

        stopwatch.Stop();

        var totalSeconds = stopwatch.Elapsed.TotalSeconds;
        var tokensPerSecond = totalSeconds > 0 ? totalTokens / totalSeconds : 0;

        return new ModelBenchmarkResult
        {
            TokensPerSecond = Math.Round(tokensPerSecond, 2),
            TotalTimeMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
            TotalTokens = totalTokens,
            PromptCount = prompts.Count
        };
    }
}
