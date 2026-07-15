using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Provedor de nuvem para o Google Drive, implementado diretamente sobre a REST API v3
/// (<c>https://www.googleapis.com/drive/v3</c>) via <see cref="HttpClient"/>, sem depender do
/// SDK oficial pesado do Google. Usa o fluxo OAuth2 "Authorization Code with PKCE" contra
/// os endpoints padrão do Google (<c>accounts.google.com</c> / <c>oauth2.googleapis.com</c>),
/// adequado para um "Aplicativo para computador" (Desktop app) — cliente público, sem
/// client secret.
/// </summary>
public sealed class GoogleDriveProvider : ICloudProvider
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DriveApiBase = "https://www.googleapis.com/drive/v3";
    private const string Scope = "https://www.googleapis.com/auth/drive.readonly";

    private readonly string _clientId;
    private readonly HttpClient _httpClient;

    public string Name => "google";

    public GoogleDriveProvider(string clientId, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new CloudProviderNotConfiguredException(
                "Configure GoogleDriveClientId em appsettings antes de usar 'cloud auth google'.");
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
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        var url = AuthorizationEndpoint + "?" + string.Join("&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return (url, verifier);
    }

    public async Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
    {
        const int port = 8765;
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
            throw new InvalidOperationException($"Falha ao trocar o código de autorização do Google Drive por um token: {body}");
        }

        var payload = JsonSerializer.Deserialize<GoogleTokenResponse>(body)
                      ?? throw new InvalidOperationException("Resposta inválida do endpoint de token do Google.");

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
        string? pageToken = null;

        do
        {
            var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed = false");
            var url = $"{DriveApiBase}/files?q={query}&fields=nextPageToken,files(id,name,size,modifiedTime,mimeType)&pageSize=1000";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Falha ao listar arquivos do Google Drive: {body}");
            }

            var page = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(body)
                       ?? new GoogleDriveFileListResponse();

            foreach (var file in page.Files ?? new List<GoogleDriveFile>())
            {
                results.Add(new CloudFileInfo
                {
                    Id = file.Id,
                    Name = file.Name,
                    SizeBytes = long.TryParse(file.Size, out var size) ? size : 0,
                    ModifiedAt = file.ModifiedTime,
                    IsFolder = file.MimeType == "application/vnd.google-apps.folder",
                });
            }

            pageToken = page.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    public async Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        var url = $"{DriveApiBase}/files/{Uri.EscapeDataString(fileId)}?alt=media";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Falha ao baixar arquivo '{fileId}' do Google Drive: {body}");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await responseStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class GoogleDriveFileListResponse
    {
        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }

        [JsonPropertyName("files")]
        public List<GoogleDriveFile>? Files { get; set; }
    }

    private sealed class GoogleDriveFile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("modifiedTime")]
        public DateTimeOffset? ModifiedTime { get; set; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }
    }
}
