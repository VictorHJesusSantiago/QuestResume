using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    private readonly LlmSamplingOptions _sampling;

    /// <param name="baseUrl">Ex.: "http://localhost:11434".</param>
    /// <param name="model">Nome do modelo Ollama (ex.: "llama3.2").</param>
    /// <param name="httpClient">
    /// Cliente HTTP opcional, útil para testes (injetar <see cref="HttpMessageHandler"/> fake).
    /// Quando omitido, um cliente próprio é criado e descartado junto com o provider.
    /// </param>
    public OllamaLlmProvider(string baseUrl, string model, HttpClient? httpClient = null, LlmSamplingOptions? sampling = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _sampling = sampling ?? LlmSamplingOptions.Default;
    }

    private OllamaOptions BuildOptions() => new(_sampling.Temperature, _sampling.TopP, _sampling.Seed);

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest(_model, prompt, false, BuildOptions());

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

    /// <summary>
    /// Streams the completion via Ollama's <c>"stream": true</c> mode, which returns one JSON
    /// object per line (NDJSON) — each with a <c>response</c> fragment, until a final object
    /// with <c>"done": true</c>.
    /// </summary>
    public async IAsyncEnumerable<string> CompleteStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new OllamaGenerateRequest(_model, prompt, true, BuildOptions());

        HttpResponseMessage response;
        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate")
            {
                Content = JsonContent.Create(request)
            };
            response = await _httpClient
                .SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
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

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line);
            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                yield return chunk.Response;
            }

            if (chunk?.Done == true)
            {
                break;
            }
        }
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
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("options")] OllamaOptions Options);

    /// <summary>
    /// Subconjunto do objeto <c>options</c> da API do Ollama (<c>POST /api/generate</c>) usado
    /// para amostragem (item 1). <c>seed</c> é omitido do JSON quando <c>null</c> (semente aleatória).
    /// </summary>
    private sealed record OllamaOptions(
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("top_p")] double TopP,
        [property: JsonPropertyName("seed")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Seed);

    private sealed record OllamaGenerateResponse(
        [property: JsonPropertyName("response")] string? Response,
        [property: JsonPropertyName("done")] bool Done);
}
