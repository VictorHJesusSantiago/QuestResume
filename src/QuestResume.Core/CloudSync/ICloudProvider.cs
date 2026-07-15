namespace QuestResume.Core.CloudSync;

/// <summary>
/// Informações mínimas de um arquivo remoto listado em um provedor de nuvem, suficientes
/// para decidir se deve ser baixado e onde salvá-lo localmente.
/// </summary>
public sealed class CloudFileInfo
{
    /// <summary>Identificador do arquivo no provedor remoto (usado em <see cref="ICloudProvider.DownloadFileAsync"/>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Nome do arquivo (incluindo extensão), usado como nome do arquivo local.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Tamanho em bytes, quando informado pelo provedor (0 se desconhecido).</summary>
    public long SizeBytes { get; set; }

    /// <summary>Data da última modificação no provedor remoto, quando disponível.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Verdadeiro quando o item é uma pasta (não deve ser baixado diretamente).</summary>
    public bool IsFolder { get; set; }
}

/// <summary>
/// Resultado do fluxo de autenticação OAuth2 (Authorization Code + PKCE) de um provedor de
/// nuvem: tokens necessários para chamadas subsequentes à API do provedor.
/// </summary>
public sealed class CloudAuthResult
{
    public string AccessToken { get; set; } = string.Empty;

    public string? RefreshToken { get; set; }

    /// <summary>Momento (UTC) em que <see cref="AccessToken"/> expira.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>
/// Abstração comum para provedores de armazenamento em nuvem (Google Drive, OneDrive,
/// Dropbox) usada por <see cref="CloudSyncService"/> para listar e baixar arquivos de uma
/// pasta remota, sem acoplar o restante do QuestResume aos SDKs/HTTP específicos de cada
/// provedor. Todas as implementações usam o fluxo OAuth2 "Authorization Code with PKCE",
/// adequado para aplicativos desktop/CLI públicos (sem client secret).
/// </summary>
public interface ICloudProvider
{
    /// <summary>Nome curto do provedor, usado nos comandos/rotas (ex.: "google", "onedrive").</summary>
    string Name { get; }

    /// <summary>
    /// Executa o fluxo OAuth2 Authorization Code + PKCE completo: gera a URL de autorização,
    /// aguarda o redirecionamento do usuário autenticado (via um listener local fornecido pelo
    /// chamador, ex. <see cref="OAuthLoopbackListener"/>) e troca o código por tokens.
    /// </summary>
    /// <param name="openBrowser">
    /// Callback invocado com a URL de autorização, para que o chamador (CLI/API/Desktop)
    /// decida como exibi-la ao usuário (abrir navegador, retornar na resposta HTTP etc.).
    /// </param>
    Task<CloudAuthResult> AuthenticateAsync(Action<string> openBrowser, CancellationToken cancellationToken = default);

    /// <summary>
    /// Troca um código de autorização já obtido (ex.: recebido via callback HTTP de um servidor
    /// web, ao contrário do fluxo interativo local de <see cref="AuthenticateAsync"/>) por tokens.
    /// </summary>
    Task<CloudAuthResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Monta a URL de autorização e o par (verifier, challenge) PKCE, sem abrir navegador nem bloquear.</summary>
    (string AuthorizationUrl, string CodeVerifier) BuildAuthorizationUrl(string redirectUri);

    /// <summary>Lista os arquivos (não-recursivo) dentro da pasta remota identificada por <paramref name="folderId"/>.</summary>
    Task<IReadOnlyList<CloudFileInfo>> ListFilesAsync(string accessToken, string folderId, CancellationToken cancellationToken = default);

    /// <summary>Baixa o conteúdo do arquivo remoto <paramref name="fileId"/> para <paramref name="destinationStream"/>.</summary>
    Task DownloadFileAsync(string accessToken, string fileId, Stream destinationStream, CancellationToken cancellationToken = default);
}
