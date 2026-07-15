using System.Net.Http.Headers;
using System.Net.Http.Json;
using QuestResume.Mobile.Models;

namespace QuestResume.Mobile.Services;

/// <summary>
/// Cliente HTTP fino para a QuestResume.Api remota. Todas as chamadas usam a URL do servidor e
/// o token JWT atualmente salvos em <see cref="SessionService"/>.
/// </summary>
public sealed class ApiClient
{
    private readonly SessionService _session;
    private readonly HttpClient _httpClient;

    public ApiClient(SessionService session)
    {
        _session = session;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = _session.ServerUrl.TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        if (!string.IsNullOrWhiteSpace(_session.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.Token);
        }

        return request;
    }

    private static async Task<string> ExtractErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch
        {
            // Corpo não era JSON — cai para a mensagem genérica abaixo.
        }

        return $"Erro HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }

    public async Task<LoginResponse> LoginAsync(string serverUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        _session.SetServerUrl(serverUrl);
        using var request = CreateRequest(HttpMethod.Post, "/api/auth/login");
        request.Content = JsonContent.Create(new LoginRequest { Username = username, Password = password });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(await ExtractErrorAsync(response));
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        return result ?? throw new ApiException("Resposta de login vazia do servidor.");
    }

    public async Task<List<SearchResultItem>> SearchAsync(string query, int? topK = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "/api/search");
        request.Content = JsonContent.Create(new SearchRequest { Query = query, TopK = topK });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(await ExtractErrorAsync(response));
        }

        var results = await response.Content.ReadFromJsonAsync<List<SearchResultItem>>(cancellationToken: cancellationToken);
        return results ?? new List<SearchResultItem>();
    }

    public async Task<AskResult> AskAsync(string question, List<ChatTurn>? history = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, "/api/ask");
        request.Content = JsonContent.Create(new AskRequest { Question = question, History = history });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(await ExtractErrorAsync(response));
        }

        var result = await response.Content.ReadFromJsonAsync<AskResult>(cancellationToken: cancellationToken);
        return result ?? throw new ApiException("Resposta de pergunta vazia do servidor.");
    }
}

public sealed class ApiException : Exception
{
    public ApiException(string message) : base(message)
    {
    }
}
