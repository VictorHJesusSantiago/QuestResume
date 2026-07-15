using QuestResume.Core.Configuration;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Baixa os arquivos de uma pasta remota de um provedor de nuvem (<see cref="ICloudProvider"/>)
/// para uma subpasta local (<c>&lt;localTargetPath&gt;/_cloud_&lt;provider&gt;/</c>), para que o
/// pipeline normal de indexação (<see cref="QuestResume.Core.Indexing.DocumentIndexer"/>) possa
/// processá-los como qualquer outro arquivo local. Não é responsável por autenticação (veja
/// <see cref="ICloudProvider.AuthenticateAsync"/> / <see cref="CloudTokenStore"/>) nem por
/// disparar a indexação em si — isso cabe ao chamador (CLI/API), reaproveitando o mesmo
/// <c>DocumentIndexer</c> já usado para pastas locais comuns.
/// </summary>
public sealed class CloudSyncService
{
    /// <summary>
    /// Baixa os arquivos (não-recursivo) da pasta remota <paramref name="remoteFolderId"/> usando
    /// <paramref name="provider"/> já autenticado (<paramref name="accessToken"/>), salvando-os em
    /// <c>&lt;localTargetPath&gt;/_cloud_&lt;provider.Name&gt;/</c>.
    /// </summary>
    public async Task<CloudSyncResult> SyncFolderAsync(
        ICloudProvider provider,
        string accessToken,
        string remoteFolderId,
        string localTargetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Nenhum token de acesso disponível para este provedor. Rode 'cloud auth' primeiro.");
        }

        var localFolder = Path.Combine(localTargetPath, $"_cloud_{provider.Name}");
        Directory.CreateDirectory(localFolder);

        var result = new CloudSyncResult { LocalFolder = localFolder };

        var files = await provider.ListFilesAsync(accessToken, remoteFolderId, cancellationToken);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.IsFolder)
            {
                result.FoldersSkipped++;
                continue;
            }

            var safeName = SanitizeFileName(file.Name);
            var destinationPath = Path.Combine(localFolder, safeName);

            try
            {
                await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
                await provider.DownloadFileAsync(accessToken, file.Id, fileStream, cancellationToken);
                result.FilesDownloaded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Sobrecarga de conveniência usada pela CLI/API: resolve o provedor e o token salvo a partir
    /// do nome do provedor e das opções/configuração compartilhadas.
    /// </summary>
    public async Task<CloudSyncResult> SyncFolderAsync(
        string providerName,
        string remoteFolderId,
        string localTargetPath,
        AppOptions options,
        CancellationToken cancellationToken = default)
    {
        var provider = CloudProviderFactory.Create(providerName, options);
        var tokenStore = new CloudTokenStore(options.IndexPath);
        var auth = tokenStore.Load(provider.Name);

        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
        {
            throw new InvalidOperationException(
                $"Nenhuma autenticação encontrada para '{provider.Name}'. Rode 'questresume cloud auth {provider.Name}' primeiro.");
        }

        return await SyncFolderAsync(provider, auth.AccessToken, remoteFolderId, localTargetPath, cancellationToken);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "arquivo_sem_nome" : sanitized;
    }
}
