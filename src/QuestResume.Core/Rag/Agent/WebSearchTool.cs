using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.Rag.Agent;

public sealed class WebSearchTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;

    public string Name => "web_search";

    public string Description =>
        "Pesquisa na web via um serviço de busca configurado pelo usuário. Use apenas quando a " +
        "pergunta exigir informação externa/atual que não está nos documentos indexados.";

    public WebSearchTool(string endpointUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            throw new InvalidOperationException(
                "WebSearchEndpointUrl não configurado. Configure um serviço de busca HTTP (ex.: SearXNG self-hosted) antes de habilitar a ferramenta de busca web.");
        }

        _endpointUrl = endpointUrl;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> InvokeAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("Informe um termo de busca.");
        }

        var separator = _endpointUrl.Contains('?') ? "&" : "?";
        var requestUrl = $"{_endpointUrl}{separator}q={Uri.EscapeDataString(input)}";

        using var response = await _httpClient.GetAsync(requestUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        WebSearchResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<WebSearchResponse>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Resposta inválida do serviço de busca web: {ex.Message}");
        }

        if (parsed?.Results is null || parsed.Results.Count == 0)
        {
            return "Nenhum resultado encontrado.";
        }

        return string.Join(
            "\n\n",
            parsed.Results.Take(5).Select(r => $"- {r.Title}\n  {r.Url}\n  {r.Snippet}"));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class WebSearchResponse
    {
        [JsonPropertyName("results")]
        public List<WebSearchResultItem>? Results { get; set; }
    }

    private sealed class WebSearchResultItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("snippet")]
        public string Snippet { get; set; } = string.Empty;
    }
}
