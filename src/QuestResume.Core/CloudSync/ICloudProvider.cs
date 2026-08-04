namespace QuestResume.Core.CloudSync;

public sealed class CloudFileInfo
{
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public long SizeBytes { get; set; }

        public DateTimeOffset? ModifiedAt { get; set; }

        public bool IsFolder { get; set; }
}

public sealed class CloudAuthResult
{
    public string AccessToken { get; set; } = string.Empty;

    public string? RefreshToken { get; set; }

        public DateTimeOffset ExpiresAtUtc { get; set; }
}

public interface ICloudProvider
{
        string Name { get; }

        Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default);

        Task<CloudAuthResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default);

        (string AuthorizationUrl, string CodeVerifier) BuildAuthorizationUrl(string redirectUri);

        Task<IReadOnlyList<CloudFileInfo>> ListFilesAsync(string accessToken, string folderId, CancellationToken cancellationToken = default);

        Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default);
}
