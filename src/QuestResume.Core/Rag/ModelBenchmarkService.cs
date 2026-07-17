using System.Diagnostics;

namespace QuestResume.Core.Rag;

/// <summary>Resultado de um benchmark local de modelo (item 7).</summary>
public sealed class ModelBenchmarkResult
{
    /// <summary>Tokens gerados por segundo (aproximado — ver <see cref="ModelBenchmarkService"/>).</summary>
    public double TokensPerSecond { get; set; }

    /// <summary>Tempo total decorrido, em milissegundos, somando todas as perguntas de teste.</summary>
    public double TotalTimeMs { get; set; }

    /// <summary>Total de tokens (aproximados) gerados durante o benchmark.</summary>
    public int TotalTokens { get; set; }

    /// <summary>Número de perguntas de teste executadas.</summary>
    public int PromptCount { get; set; }
}

/// <summary>
/// Mede a velocidade de geração (tokens/segundo) do modelo configurado (item 7) rodando um
/// conjunto fixo de perguntas de teste contra um <see cref="ILlmProvider"/> via streaming, o que
/// permite contar tokens à medida que são gerados. A contagem de "tokens" é APROXIMADA: usa a
/// contagem de fragmentos retornados pelo stream (que no LLamaSharp corresponde a ~1 token cada,
/// mas no Ollama pode agrupar), o que é suficiente para uma métrica comparativa relativa entre
/// modelos/configurações — não é uma medida exata de tokens do tokenizador.
/// </summary>
public sealed class ModelBenchmarkService
{
    private readonly ILlmProvider _llmProvider;

    /// <summary>Perguntas curtas e fixas usadas no benchmark (em PT-BR).</summary>
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

    /// <summary>
    /// Roda o benchmark. Cada prompt é submetido via streaming e os fragmentos são contados como
    /// aproximação de tokens. Retorna a média de tokens/segundo agregada sobre todos os prompts.
    /// </summary>
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
