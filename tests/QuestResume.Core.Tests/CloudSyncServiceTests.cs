using System.Text;
using QuestResume.Core.CloudSync;

namespace QuestResume.Core.Tests;

/// <summary>
/// Fake em memória de <see cref="ICloudProvider"/> usado para testar <see cref="CloudSyncService"/>
/// sem depender de rede real ou de credenciais OAuth de um provedor de nuvem verdadeiro.
/// </summary>
internal sealed class FakeCloudProvider : ICloudProvider
{
    public string Name { get; }

    public List<CloudFileInfo> Files { get; } = new();

    public Dictionary<string, string> FileContents { get; } = new();

    public bool ThrowOnDownloadFor { get; set; }

    public string? FailingFileId { get; set; }

    public FakeCloudProvider(string name = "fake")
    {
        Name = name;
    }

    public Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default)
        => Task.FromResult(new CloudAuthResult { AccessToken = "fake-token", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) });

    public Task<CloudAuthResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default)
        => Task.FromResult(new CloudAuthResult { AccessToken = "fake-token", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) });

    public (string AuthorizationUrl, string CodeVerifier) BuildAuthorizationUrl(string redirectUri)
        => ("https://example.invalid/auth", "verifier");

    public Task<IReadOnlyList<CloudFileInfo>> ListFilesAsync(string accessToken, string folderId, CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyList<CloudFileInfo>)Files);

    public Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default)
    {
        if (ThrowOnDownloadFor && fileId == FailingFileId)
        {
            throw new InvalidOperationException("Falha simulada de download.");
        }

        var content = FileContents.TryGetValue(fileId, out var text) ? text : string.Empty;
        var bytes = Encoding.UTF8.GetBytes(content);
        return destinationStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }
}

public sealed class CloudSyncServiceTests : IDisposable
{
    private readonly string _tempDir;

    public CloudSyncServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "questresume-cloudsync-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SyncFolderAsync_DownloadsFilesIntoCloudSubfolder()
    {
        var provider = new FakeCloudProvider("google");
        provider.Files.Add(new CloudFileInfo { Id = "1", Name = "documento1.txt" });
        provider.Files.Add(new CloudFileInfo { Id = "2", Name = "documento2.txt" });
        provider.FileContents["1"] = "conteúdo do primeiro arquivo";
        provider.FileContents["2"] = "conteúdo do segundo arquivo";

        var service = new CloudSyncService();
        var result = await service.SyncFolderAsync(provider, "token-abc", "pasta-remota-id", _tempDir);

        Assert.Equal(2, result.FilesDownloaded);
        Assert.Empty(result.Errors);
        Assert.Equal(Path.Combine(_tempDir, "_cloud_google"), result.LocalFolder);

        var downloadedFile1 = Path.Combine(result.LocalFolder, "documento1.txt");
        var downloadedFile2 = Path.Combine(result.LocalFolder, "documento2.txt");
        Assert.True(File.Exists(downloadedFile1));
        Assert.True(File.Exists(downloadedFile2));
        Assert.Equal("conteúdo do primeiro arquivo", await File.ReadAllTextAsync(downloadedFile1));
        Assert.Equal("conteúdo do segundo arquivo", await File.ReadAllTextAsync(downloadedFile2));
    }

    [Fact]
    public async Task SyncFolderAsync_SkipsFolders()
    {
        var provider = new FakeCloudProvider("onedrive");
        provider.Files.Add(new CloudFileInfo { Id = "f1", Name = "subpasta", IsFolder = true });
        provider.Files.Add(new CloudFileInfo { Id = "f2", Name = "arquivo.txt" });
        provider.FileContents["f2"] = "conteúdo";

        var service = new CloudSyncService();
        var result = await service.SyncFolderAsync(provider, "token", "pasta-remota-id", _tempDir);

        Assert.Equal(1, result.FilesDownloaded);
        Assert.Equal(1, result.FoldersSkipped);
    }

    [Fact]
    public async Task SyncFolderAsync_RecordsErrorsWithoutStoppingOtherFiles()
    {
        var provider = new FakeCloudProvider("google");
        provider.Files.Add(new CloudFileInfo { Id = "bad", Name = "falha.txt" });
        provider.Files.Add(new CloudFileInfo { Id = "ok", Name = "sucesso.txt" });
        provider.FileContents["ok"] = "ok";
        provider.ThrowOnDownloadFor = true;
        provider.FailingFileId = "bad";

        var service = new CloudSyncService();
        var result = await service.SyncFolderAsync(provider, "token", "pasta-remota-id", _tempDir);

        Assert.Equal(1, result.FilesDownloaded);
        Assert.Single(result.Errors);
        Assert.True(File.Exists(Path.Combine(result.LocalFolder, "sucesso.txt")));
    }

    [Fact]
    public async Task SyncFolderAsync_ThrowsWhenAccessTokenMissing()
    {
        var provider = new FakeCloudProvider("google");
        var service = new CloudSyncService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncFolderAsync(provider, string.Empty, "pasta-remota-id", _tempDir));
    }

    [Fact]
    public void CloudProviderFactory_ThrowsClearPortugueseErrorWhenClientIdMissing()
    {
        var options = new QuestResume.Core.Configuration.AppOptions();

        var ex = Assert.Throws<CloudProviderNotConfiguredException>(() =>
            CloudProviderFactory.Create("google", options));

        Assert.Contains("GoogleDriveClientId", ex.Message);
        Assert.Contains("cloud auth google", ex.Message);
    }

    [Fact]
    public void CloudProviderFactory_ThrowsClearPortugueseErrorForOneDriveWhenClientIdMissing()
    {
        var options = new QuestResume.Core.Configuration.AppOptions();

        var ex = Assert.Throws<CloudProviderNotConfiguredException>(() =>
            CloudProviderFactory.Create("onedrive", options));

        Assert.Contains("OneDriveClientId", ex.Message);
    }

    [Fact]
    public async Task SyncFolderAsync_ByProviderName_ThrowsWhenNoTokenSaved()
    {
        var options = new QuestResume.Core.Configuration.AppOptions
        {
            GoogleDriveClientId = "fake-client-id",
            IndexPath = _tempDir,
        };

        var service = new CloudSyncService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncFolderAsync("google", "pasta-remota-id", _tempDir, options));
    }

    [Fact]
    public void CloudTokenStore_SavesAndLoadsTokenPerProvider()
    {
        var store = new CloudTokenStore(_tempDir);
        var auth = new CloudAuthResult { AccessToken = "token-123", RefreshToken = "refresh-abc", ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) };

        store.Save("google", auth);
        var loaded = store.Load("google");

        Assert.NotNull(loaded);
        Assert.Equal("token-123", loaded!.AccessToken);
        Assert.Equal("refresh-abc", loaded.RefreshToken);

        Assert.Null(store.Load("onedrive"));
    }
}
