using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.Rag;

/// <summary>
/// <see cref="ILlmProvider"/> backed by a local Ollama server
/// (<c>POST {baseUrl}/api/generate</c>). Ollama runs entirely on the user's machine; no data
/// leaves the host.
/// </summary>
public sealed class OllamaLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly string _model;

    /// <param name="baseUrl">Ex.: "http://localhost:11434".</param>
    /// <param name="model">Nome do modelo Ollama (ex.: "llama3.2").</param>
    /// <param name="httpClient">
    /// Cliente HTTP opcional, útil para testes (injetar <see cref="HttpMessageHandler"/> fake).
    /// Quando omitido, um cliente próprio é criado e descartado junto com o provider.
    /// </param>
    public OllamaLlmProvider(string baseUrl, string model, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest(_model, prompt, false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .PostAsJsonAsync($"{_baseUrl}/api/generate", request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OllamaNotAvailableException(_baseUrl, ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OllamaNotAvailableException(_baseUrl);
        }

        var result = await response.Content
            .ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result?.Response?.Trim() ?? string.Empty;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private sealed record OllamaGenerateRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("prompt")] string Prompt,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}
