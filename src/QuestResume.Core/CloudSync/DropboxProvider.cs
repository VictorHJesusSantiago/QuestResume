using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Provedor de nuvem para o Dropbox, implementado diretamente sobre a API v2
/// (<c>https://api.dropboxapi.com/2</c> / <c>https://content.dropboxapi.com/2</c>) via
/// <see cref="HttpClient"/>, sem depender do SDK oficial. Usa o fluxo OAuth2 "Authorization
/// Code with PKCE" contra <c>https://www.dropbox.com/oauth2/authorize</c> /
/// <c>https://api.dropboxapi.com/oauth2/token</c>, adequado para um app público (sem client
/// secret) registrado no App Console do Dropbox.
/// </summary>
public sealed class DropboxProvider : ICloudProvider
{
    private const string AuthorizationEndpoint = "https://www.dropbox.com/oauth2/authorize";
    private const string TokenEndpoint = "https://api.dropboxapi.com/oauth2/token";
    private const string ApiBase = "https://api.dropboxapi.com/2";
    private const string ContentApiBase = "https://content.dropboxapi.com/2";

    private readonly string _clientId;
    private readonly HttpClient _httpClient;

    public string Name => "dropbox";

    public DropboxProvider(string clientId, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new CloudProviderNotConfiguredException(
                "Configure DropboxClientId em appsettings/config antes de usar 'cloud auth dropbox'. " +
                "Veja CloudSync/README.md para o passo a passo de criação do app no App Console do Dropbox.");
        }

        _clientId = clientId;
        _httpClient = httpClient ?? new HttpClient();
    }

    public (string AuthorizationUrl, string CodeVerifier) BuildAuthorizationUrl(string redirectUri)
    {
        var verifier = PkceHelper.GenerateCodeVerifier();
        var challenge = PkceHelper.GenerateCodeChallenge(verifier);

        var query = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["token_access_type"] = "offline",
        };

        var url = AuthorizationEndpoint + "?" + string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return (url, verifier);
    }

    public async Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        const int port = 8767;
        var redirectUri = $"http://localhost:{port}/callback/";
        var (authUrl, verifier) = BuildAuthorizationUrl(redirectUri);

        openBrowser(authUrl);

        var code = await OAuthLoopbackListener.WaitForAuthorizationCodeAsync(port, cancellationToken);
        return await ExchangeCodeAsync(code, verifier, redirectUri, cancellationToken);
    }

    public async Task<CloudAuthResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        };

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao trocar o código de autorização do Dropbox por um token: {body}");
        }

        var payload = JsonSerializer.Deserialize<DropboxTokenResponse>(body)
                      ?? throw new InvalidOperationException("Resposta inválida do endpoint de token do Dropbox.");

        return new CloudAuthResult
        {
            AccessToken = payload.AccessToken,
            RefreshToken = payload.RefreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(payload.ExpiresIn),
        };
    }

    public async Task<IReadOnlyList<CloudFileInfo>> ListFilesAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
    {
        // A API do Dropbox usa caminhos (ex.: "/Documentos") em vez de IDs de pasta como
        // Google Drive/OneDrive; "folderId" aqui é tratado como o caminho da pasta remota.
        // Caminho vazio ou "root"/"/" representa a raiz do Dropbox do usuário (string vazia na API).
        var path = string.IsNullOrWhiteSpace(folderId) || folderId is "root" or "/" ? string.Empty : folderId;

        var results = new List<CloudFileInfo>();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/files/list_folder");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(new { path, recursive = false });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Falha ao listar arquivos do Dropbox: {body}");
        }

        var page = JsonSerializer.Deserialize<DropboxListFolderResponse>(body) ?? new DropboxListFolderResponse();
        AppendEntries(results, page.Entries);

        var cursor = page.Cursor;
        while (page.HasMore && !string.IsNullOrEmpty(cursor))
        {
            using var continueRequest = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/files/list_folder/continue");
            continueRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            continueRequest.Content = JsonContent.Create(new { cursor });

            using var continueResponse = await _httpClient.SendAsync(continueRequest, cancellationToken);
            var continueBody = await continueResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!continueResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Falha ao continuar a listagem de arquivos do Dropbox: {continueBody}");
            }

            page = JsonSerializer.Deserialize<DropboxListFolderResponse>(continueBody) ?? new DropboxListFolderResponse();
            AppendEntries(results, page.Entries);
            cursor = page.Cursor;
        }

        return results;
    }

    private static void AppendEntries(List<CloudFileInfo> results, List<DropboxEntry>? entries)
    {
        foreach (var entry in entries ?? new List<DropboxEntry>())
        {
            results.Add(new CloudFileInfo
            {
                Id = entry.PathLower ?? entry.Name,
                Name = entry.Name,
                SizeBytes = entry.Size,
                ModifiedAt = entry.ServerModified,
                IsFolder = entry.Tag == "folder",
            });
        }
    }

    public async Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ContentApiBase}/files/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new { path = fileId }));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao baixar arquivo '{fileId}' do Dropbox: {body}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private sealed class DropboxTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class DropboxListFolderResponse
    {
        [JsonPropertyName("entries")]
        public List<DropboxEntry>? Entries { get; set; }

        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }

        [JsonPropertyName("has_more")]
        public bool HasMore { get; set; }
    }

    private sealed class DropboxEntry
    {
        [JsonPropertyName(".tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("path_lower")]
        public string? PathLower { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("server_modified")]
        public DateTimeOffset? ServerModified { get; set; }
    }
}
