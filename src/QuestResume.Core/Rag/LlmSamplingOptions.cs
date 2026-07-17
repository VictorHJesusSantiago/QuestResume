namespace QuestResume.Core.Rag;

/// <summary>
/// Parâmetros de amostragem repassados aos provedores de LLM (item 1): temperatura, top-p
/// (nucleus) e semente opcional. Centralizados em um único tipo para não multiplicar parâmetros
/// nos construtores de <see cref="LocalLlmService"/>, <see cref="LlamaSharpLlmProvider"/> e
/// <see cref="OllamaLlmProvider"/>.
/// </summary>
public sealed record LlmSamplingOptions(double Temperature = 0.8, double TopP = 0.9, int? Seed = null)
{
    /// <summary>Valores padrão do LLamaSharp/Ollama (temperatura 0.8, top-p 0.9, semente aleatória).</summary>
    public static readonly LlmSamplingOptions Default = new();
}
