using QuestResume.Core.Configuration;

namespace QuestResume.Core.CloudSync;

public static class CloudProviderFactory
{
    public static readonly string[] SupportedProviders = { "google", "onedrive", "dropbox" };

    public static ICloudProvider Create(string providerName, AppOptions options, HttpClient? httpClient = null)
    {
        return (providerName?.ToLowerInvariant()) switch
        {
            "google" or "googledrive" or "drive" => new GoogleDriveProvider(options.GoogleDriveClientId, httpClient),
            "onedrive" or "microsoft" => new OneDriveProvider(options.OneDriveClientId, httpClient),
            "dropbox" => new DropboxProvider(options.DropboxClientId, httpClient),
            _ => throw new CloudProviderNotConfiguredException(
                $"Provedor de nuvem desconhecido: '{providerName}'. Provedores suportados: {string.Join(", ", SupportedProviders)}."),
        };
    }
}
