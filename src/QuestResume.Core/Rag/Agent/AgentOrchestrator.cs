using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.Rag.Agent;

/// <summary>
/// Orquestra o uso opcional de ferramentas (<see cref="ITool"/>) por um <see cref="ILlmProvider"/>:
/// pede ao LLM que escolha, em JSON estrito, no máximo uma ferramenta para responder a uma
/// pergunta; executa a ferramenta escolhida (com parse defensivo — JSON inválido ou ferramenta
/// desconhecida simplesmente resulta em "nenhuma ferramenta usada"); e por fim pede ao LLM uma
/// resposta final incorporando o resultado da ferramenta ao contexto.
/// </summary>
public sealed class AgentOrchestrator
{
    private const int MaxParseAttempts = 2;

    private readonly ILlmProvider _llm;
    private readonly IReadOnlyList<ITool> _tools;

    public AgentOrchestrator(ILlmProvider llm, IReadOnlyList<ITool> tools)
    {
        _llm = llm;
        _tools = tools;
    }

    /// <summary>
    /// Executa o fluxo completo: escolha de ferramenta (até <see cref="MaxParseAttempts"/>
    /// tentativas de obter um JSON válido do LLM) → no máximo 1 execução de ferramenta →
    /// resposta final do LLM já incorporando o resultado da ferramenta, se houver.
    /// </summary>
    public async Task<AgentResult> RunAsync(string question, CancellationToken cancellationToken = default)
    {
        var toolChoice = await ChooseToolAsync(question, cancellationToken).ConfigureAwait(false);

        if (toolChoice is null || toolChoice.Tool.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            var directAnswer = await _llm.CompleteAsync(question, cancellationToken).ConfigureAwait(false);
            return new AgentResult { Answer = directAnswer, ToolUsed = null, ToolOutput = null };
        }

        var tool = _tools.FirstOrDefault(t => t.Name.Equals(toolChoice.Tool, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            // Ferramenta desconhecida escolhida pelo LLM: ignora e responde sem ferramenta.
            var fallbackAnswer = await _llm.CompleteAsync(question, cancellationToken).ConfigureAwait(false);
            return new AgentResult { Answer = fallbackAnswer, ToolUsed = null, ToolOutput = null };
        }

        string toolOutput;
        try
        {
            toolOutput = await tool.InvokeAsync(toolChoice.Input ?? string.Empty, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            toolOutput = $"Erro ao executar a ferramenta '{tool.Name}': {ex.Message}";
        }

        var finalPrompt =
            $"Pergunta original: {question}\n\n" +
            $"Resultado da ferramenta '{tool.Name}' (entrada: \"{toolChoice.Input}\"):\n{toolOutput}\n\n" +
            "Com base nesse resultado, responda à pergunta original de forma direta e em português.";

        var finalAnswer = await _llm.CompleteAsync(finalPrompt, cancellationToken).ConfigureAwait(false);

        return new AgentResult { Answer = finalAnswer, ToolUsed = tool.Name, ToolOutput = toolOutput };
    }

    private async Task<ToolChoice?> ChooseToolAsync(string question, CancellationToken cancellationToken)
    {
        var toolDescriptions = string.Join("\n", _tools.Select(t => $"- \"{t.Name}\": {t.Description}"));

        var selectionPrompt =
            "Você é um assistente com acesso às seguintes ferramentas opcionais:\n" +
            $"{toolDescriptions}\n\n" +
            "Dada a pergunta do usuário abaixo, decida se alguma ferramenta é necessária. " +
            "Responda APENAS com um objeto JSON estrito, sem texto adicional, sem markdown, em uma das formas:\n" +
            "{\"tool\": \"<nome_da_ferramenta>\", \"input\": \"<argumento>\"}\n" +
            "{\"tool\": \"none\"}\n\n" +
            $"Pergunta: {question}";

        for (var attempt = 0; attempt < MaxParseAttempts; attempt++)
        {
            var raw = await _llm.CompleteAsync(selectionPrompt, cancellationToken).ConfigureAwait(false);
            var parsed = TryParseToolChoice(raw);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        // Depois de esgotar as tentativas, assume "nenhuma ferramenta" em vez de travar o fluxo.
        return null;
    }

    /// <summary>
    /// Parse defensivo da escolha de ferramenta do LLM: extrai o primeiro objeto JSON encontrado
    /// no texto (o LLM às vezes envolve a resposta em markdown/texto extra), valida contra o
    /// esquema esperado e ignora silenciosamente qualquer coisa que não faça sentido.
    /// </summary>
    internal static ToolChoice? TryParseToolChoice(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        var jsonSlice = raw.Substring(start, end - start + 1);

        DecisionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DecisionDto>(jsonSlice, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Tool))
        {
            return null;
        }

        return new ToolChoice(dto.Tool.Trim(), dto.Input);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class DecisionDto
    {
        [JsonPropertyName("tool")]
        public string? Tool { get; set; }

        [JsonPropertyName("input")]
        public string? Input { get; set; }
    }
}

/// <summary>Escolha de ferramenta feita pelo LLM, já validada estruturalmente.</summary>
public sealed record ToolChoice(string Tool, string? Input);

/// <summary>Resultado de uma execução do agente: resposta final e, se aplicável, a ferramenta usada.</summary>
public sealed class AgentResult
{
    public string Answer { get; set; } = string.Empty;
    public string? ToolUsed { get; set; }
    public string? ToolOutput { get; set; }
}
