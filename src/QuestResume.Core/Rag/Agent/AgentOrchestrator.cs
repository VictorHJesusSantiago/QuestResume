using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.Rag.Agent;

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

        
        return null;
    }

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

public sealed record ToolChoice(string Tool, string? Input);

public sealed class AgentResult
{
    public string Answer { get; set; } = string.Empty;
    public string? ToolUsed { get; set; }
    public string? ToolOutput { get; set; }
}
