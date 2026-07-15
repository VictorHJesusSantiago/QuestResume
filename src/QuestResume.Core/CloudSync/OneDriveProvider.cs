using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Client;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Provedor de nuvem para o OneDrive/Microsoft Graph. O fluxo interativo
/// (<see cref="AuthenticateAsync"/>, usado pela CLI) delega a <c>Microsoft.Identity.Client</c>
/// (MSAL), que já implementa Authorization Code + PKCE e o listener loopback local
/// internamente. O fluxo manual (<see cref="BuildAuthorizationUrl"/> + <see cref="ExchangeCodeAsync"/>,
/// usado pelo endpoint <c>GET /api/cloud/onedrive/callback</c> da API, onde o redirecionamento
/// chega via HTTP e não pode ser interceptado pelo listener interno do MSAL) fala diretamente
/// com o endpoint de token do Microsoft Identity Platform v2.0.
/// </summary>
public sealed class OneDriveProvider : ICloudProvider
{
    private const string AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string GraphApiBase = "https://graph.microsoft.com/v1.0";
    private static readonly string[] Scopes = { "Files.Read.All" };

    private readonly string _clientId;
    private readonly HttpClient _httpClient;
    private readonly IPublicClientApplication _msalApp;

    public string Name => "onedrive";

    public OneDriveProvider(string clientId, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new CloudProviderNotConfiguredException(
                "Configure OneDriveClientId em appsettings antes de usar 'cloud auth onedrive'.");
        }

        _clientId = clientId;
        _httpClient = httpClient ?? new HttpClient();
        _msalApp = PublicClientApplicationBuilder.Create(_clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, "common")
            .WithDefaultRedirectUri()
            .Build();
    }

    public (string AuthorizationUrl, string CodeVerifier) BuildAuthorizationUrl(string redirectUri)
    {
        var verifier = PkceHelper.GenerateCodeVerifier();
        var challenge = PkceHelper.GenerateCodeChallenge(verifier);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', Scopes) + " offline_access",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        };

        var url = AuthorizationEndpoint + "?" + string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return (url, verifier);
    }

    /// <summary>
    /// Fluxo interativo local (CLI): usa MSAL (<see cref="IPublicClientApplication.AcquireTokenInteractive"/>),
    /// que abre o navegador via <paramref name="openBrowser"/> e recebe o redirecionamento no
    /// loopback local gerenciado internamente pela biblioteca.
    /// </summary>
    public async Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        var result = await _msalApp.AcquireTokenInteractive(Scopes)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                OpenBrowserAsync = uri =>
                {
                    openBrowser(uri.ToString());
                    return Task.CompletedTask;
                },
            })
            .ExecuteAsync(cancellationToken);

        return new CloudAuthResult
        {
            AccessToken = result.AccessToken,
            RefreshToken = null, // MSAL gerencia o refresh internamente via seu próprio token cache.
            ExpiresAtUtc = result.ExpiresOn,
        };
    }

    /// <summary>
    /// Troca manual do código de autorização por tokens, usada pelo endpoint HTTP de callback da
    /// API (onde não é possível usar o loopback interno do MSAL, pois o redirecionamento chega em
    /// uma rota ASP.NET já existente).
    /// </summary>
    public async Task<CloudAuthResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = string.Join(' ', Scopes) + " offline_access",
        };

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao trocar o código de autorização do OneDrive por um token: {body}");
        }

        var payload = JsonSerializer.Deserialize<MicrosoftTokenResponse>(body)
                      ?? throw new InvalidOperationException("Resposta inválida do endpoint de token da Microsoft.");

        return new CloudAuthResult
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
        };
    }

    public async Task<IReadOnlyList<CloudFileInfo>> ListFilesAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
    {
        var results = new List<CloudFileInfo>();
        var url = $"{GraphApiBase}/me/drive/items/{Uri.EscapeDataString(folderId)}/children?$select=id,name,size,lastModifiedDateTime,folder";

        while (!string.IsNullOrEmpty(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Falha ao listar arquivos do OneDrive: {body}");
            }

            var page = JsonSerializer.Deserialize<GraphChildrenResponse>(body) ?? new GraphChildrenResponse();
            foreach (var item in page.Value ?? new List<GraphDriveItem>())
            {
                results.Add(new CloudFileInfo
                {
                    Id = item.Id,
                    Name = item.Name,
                    SizeBytes = item.Size,
                    ModifiedAt = item.LastModifiedDateTime,
                    IsFolder = item.Folder is not null,
                });
            }

            url = page.NextLink ?? string.Empty;
        }

        return results;
    }

    public async Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        var url = $"{GraphApiBase}/me/drive/items/{Uri.EscapeDataString(fileId)}/content";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao baixar arquivo '{fileId}' do OneDrive: {body}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private sealed class MicrosoftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class GraphChildrenResponse
    {
        [JsonPropertyName("value")]
        public List<GraphDriveItem>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    private sealed class GraphDriveItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public DateTimeOffset? LastModifiedDateTime { get; set; }

        [JsonPropertyName("folder")]
        public object? Folder { get; set; }
    }
}
