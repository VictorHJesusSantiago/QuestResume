using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QuestResume.Core.CloudSync;
using QuestResume.Core.Configuration;

namespace QuestResume.Core.Tests;

/// <summary>
/// <see cref="HttpMessageHandler"/> em memória que responde com corpos JSON pré-programados por
/// URL (sem depender de rede real), usado para testar <see cref="DropboxProvider"/>.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responsesByUrlContains = new();

    /// <summary>Snapshot (método, URL, corpo lido como texto, cabeçalhos) de cada requisição recebida.</summary>
    public List<(HttpMethod Method, string Url, string Body, HttpRequestHeaders Headers)> Requests { get; } = new();

    public void SetResponse(string urlContains, string body, HttpStatusCode status = HttpStatusCode.OK)
        => _responsesByUrlContains[urlContains] = (status, body);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        var requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, url, requestBody, request.Headers));

        foreach (var (key, (status, body)) in _responsesByUrlContains)
        {
            if (url.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
    }
}

public sealed class DropboxProviderTests
{
    [Fact]
    public void Constructor_ThrowsClearPortugueseErrorWhenClientIdMissing()
    {
        var ex = Assert.Throws<CloudProviderNotConfiguredException>(() => new DropboxProvider(string.Empty));
        Assert.Contains("DropboxClientId", ex.Message);
        Assert.Contains("cloud auth dropbox", ex.Message);
    }

    [Fact]
    public void CloudProviderFactory_CreatesDropboxProvider()
    {
        var options = new AppOptions { DropboxClientId = "app-key-123" };
        var provider = CloudProviderFactory.Create("dropbox", options);

        Assert.Equal("dropbox", provider.Name);
        Assert.IsType<DropboxProvider>(provider);
    }

    [Fact]
    public void CloudProviderFactory_ThrowsClearPortugueseErrorForDropboxWhenClientIdMissing()
    {
        var options = new AppOptions();
        var ex = Assert.Throws<CloudProviderNotConfiguredException>(() => CloudProviderFactory.Create("dropbox", options));
        Assert.Contains("DropboxClientId", ex.Message);
    }

    [Fact]
    public void BuildAuthorizationUrl_IncludesPkceChallengeAndClientId()
    {
        var provider = new DropboxProvider("app-key-123");
        var (url, verifier) = provider.BuildAuthorizationUrl("http://localhost:8767/callback/");

        Assert.StartsWith("https://www.dropbox.com/oauth2/authorize", url);
        Assert.Contains("client_id=app-key-123", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.False(string.IsNullOrWhiteSpace(verifier));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ParsesTokenResponse()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse("oauth2/token", JsonSerializer.Serialize(new
        {
            access_token = "access-abc",
            refresh_token = "refresh-xyz",
            expires_in = 14400,
        }));

        var provider = new DropboxProvider("app-key-123", new HttpClient(handler));
        var result = await provider.ExchangeCodeAsync("auth-code", "verifier", "http://localhost:8767/callback/");

        Assert.Equal("access-abc", result.AccessToken);
        Assert.Equal("refresh-xyz", result.RefreshToken);
        Assert.True(result.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ListFilesAsync_ParsesEntriesAndTreatsRootPathCorrectly()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse("files/list_folder", """
            {
              "entries": [
                { ".tag": "file", "name": "relatorio.pdf", "path_lower": "/relatorio.pdf", "size": 1024, "server_modified": "2026-01-01T00:00:00Z" },
                { ".tag": "folder", "name": "Contratos", "path_lower": "/contratos", "size": 0 }
              ],
              "cursor": null,
              "has_more": false
            }
            """);

        var provider = new DropboxProvider("app-key-123", new HttpClient(handler));
        var files = await provider.ListFilesAsync("token-abc", "root");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Name == "relatorio.pdf" && !f.IsFolder && f.SizeBytes == 1024);
        Assert.Contains(files, f => f.Name == "Contratos" && f.IsFolder);

        var listRequest = Assert.Single(handler.Requests, r => r.Url.Contains("list_folder") && !r.Url.Contains("continue"));
        Assert.Contains("\"path\":\"\"", listRequest.Body);
    }

    [Fact]
    public async Task DownloadFileAsync_WritesResponseBodyToDestinationStream()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse("files/download", "conteúdo do arquivo");

        var provider = new DropboxProvider("app-key-123", new HttpClient(handler));
        using var destination = new MemoryStream();
        await provider.DownloadFileAsync("token-abc", "/relatorio.pdf", destination);

        destination.Position = 0;
        var text = new StreamReader(destination, Encoding.UTF8).ReadToEnd();
        Assert.Equal("conteúdo do arquivo", text);

        var downloadRequest = Assert.Single(handler.Requests, r => r.Url.Contains("files/download"));
        Assert.True(downloadRequest.Headers.Contains("Dropbox-API-Arg"));
    }
}

public sealed class CloudOAuthStateStoreTests
{
    [Fact]
    public void Save_ThenTryConsume_ReturnsStoredValues()
    {
        var state = CloudOAuthStateStore.Save("dropbox", "verifier-abc", "http://localhost:8767/callback/");

        var found = CloudOAuthStateStore.TryConsume(state, "dropbox", out var codeVerifier, out var redirectUri);

        Assert.True(found);
        Assert.Equal("verifier-abc", codeVerifier);
        Assert.Equal("http://localhost:8767/callback/", redirectUri);
    }

    [Fact]
    public void TryConsume_IsSingleUse()
    {
        var state = CloudOAuthStateStore.Save("google", "verifier-1", "http://localhost:8765/callback/");

        Assert.True(CloudOAuthStateStore.TryConsume(state, "google", out _, out _));
        Assert.False(CloudOAuthStateStore.TryConsume(state, "google", out _, out _));
    }

    [Fact]
    public void TryConsume_ReturnsFalseForWrongProvider()
    {
        var state = CloudOAuthStateStore.Save("onedrive", "verifier-2", "http://localhost:8766/callback/");

        Assert.False(CloudOAuthStateStore.TryConsume(state, "dropbox", out _, out _));
    }

    [Fact]
    public void TryConsume_ReturnsFalseForUnknownState()
    {
        Assert.False(CloudOAuthStateStore.TryConsume(Guid.NewGuid().ToString("N"), "google", out _, out _));
    }
}
