using System.IO.Compression;
using QuestResume.Core.Security;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Mantém o índice Lucene.NET criptografado em repouso. Estratégia adotada (mais simples que
/// integra sem tocar no Lucene.NET em si, que grava vários arquivos binários por segmento e não
/// oferece um <c>Directory</c> criptografado pronto para uso): o índice é aberto/gravado em uma
/// pasta de trabalho temporária em texto claro durante o uso normal da aplicação; ao encerrar
/// (ou ficar ocioso), a pasta é compactada (zip) e criptografada com AES-256-GCM em um único
/// arquivo <c>index.enc</c> dentro de <c>IndexPath</c>, e a pasta de trabalho é apagada. Ao
/// abrir, se existir <c>index.enc</c>, ele é decriptado e descompactado de volta para a pasta de
/// trabalho antes de qualquer leitura/escrita Lucene. Assim, nenhum arquivo do índice
/// (segmentos, dicionário de termos etc.) fica legível em disco fora de uma sessão ativa.
/// </summary>
public static class LuceneIndexEncryptionService
{
    public const string EncryptedFileName = "index.enc";

    /// <summary>
    /// Se um <c>index.enc</c> existir em <paramref name="indexPath"/>, decripta e extrai seu
    /// conteúdo para <paramref name="workingPath"/>, retornando <c>true</c>. Se não existir
    /// (primeira indexação), não faz nada e retorna <c>false</c>.
    /// </summary>
    public static bool OpenIntoWorkingFolder(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        var encPath = Path.Combine(indexPath, EncryptedFileName);
        if (!File.Exists(encPath))
        {
            return false;
        }

        // Already open: plain Lucene segment files are present in the working folder (typical
        // when workingPath == indexPath and a previous call in the same session already
        // decrypted it). Avoid clobbering in-progress writes with a redundant extraction.
        if (Directory.Exists(workingPath) && Directory.GetFiles(workingPath, "segments_*").Length > 0)
        {
            return true;
        }

        Directory.CreateDirectory(workingPath);
        var key = MasterKeyManager.DeriveKey(masterPassword, salt);
        var zipBytes = AesCryptoHelper.Decrypt(File.ReadAllBytes(encPath), key);

        var tempZip = Path.Combine(Path.GetTempPath(), $"questresume_idx_{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllBytes(tempZip, zipBytes);
            ZipFile.ExtractToDirectory(tempZip, workingPath, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }

        return true;
    }

    /// <summary>
    /// Compacta o conteúdo de <paramref name="workingPath"/> e grava o resultado criptografado
    /// como <c>index.enc</c> em <paramref name="indexPath"/>, apagando em seguida os arquivos em
    /// texto claro da pasta de trabalho.
    /// </summary>
    public static void SealFromWorkingFolder(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        if (!Directory.Exists(workingPath))
        {
            return;
        }

        Directory.CreateDirectory(indexPath);
        var key = MasterKeyManager.DeriveKey(masterPassword, salt);
        var encPath = Path.Combine(indexPath, EncryptedFileName);

        // When workingPath == indexPath (the common case: the encrypted index is kept unpacked
        // in-place while in use), a stale index.enc from a previous seal would otherwise get
        // zipped into the new one recursively. Remove it first — it's about to be replaced.
        if (File.Exists(encPath))
        {
            File.Delete(encPath);
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"questresume_idx_{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            ZipFile.CreateFromDirectory(workingPath, tempZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            var zipBytes = File.ReadAllBytes(tempZip);
            var encrypted = AesCryptoHelper.Encrypt(zipBytes, key);
            File.WriteAllBytes(encPath, encrypted);
        }
        finally
        {
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }
        }

        // Remove os arquivos em texto claro da pasta de trabalho, preservando o index.enc recém
        // gravado (necessário quando workingPath == indexPath, já que apagar a pasta inteira
        // apagaria o próprio index.enc que acabamos de criar nela).
        foreach (var file in Directory.GetFiles(workingPath))
        {
            if (string.Equals(Path.GetFileName(file), EncryptedFileName, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Arquivo em uso (ex.: vectors.db com conexão SQLite ainda aberta, ou handle do
                // Lucene ainda não liberado) — permanece em texto claro; a próxima chamada a
                // SealFromWorkingFolder tentará novamente. index.enc já reflete o conteúdo atual.
            }
        }

        foreach (var dir in Directory.GetDirectories(workingPath))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Ver comentário acima.
            }
        }
    }

    /// <summary>Wrapper assíncrono de <see cref="OpenIntoWorkingFolder"/> para chamadores async.</summary>
    public static Task<bool> OpenAsync(string indexPath, string workingPath, string masterPassword, byte[] salt)
        => Task.FromResult(OpenIntoWorkingFolder(indexPath, workingPath, masterPassword, salt));

    /// <summary>
    /// Wrapper assíncrono de <see cref="SealFromWorkingFolder"/> — o ponto de extensão explícito
    /// que outros chamadores (CLI, hosted services, etc.) podem invocar para forçar o
    /// selamento/criptografia do índice fora do fluxo normal de <c>IndexFolderAsync</c>.
    /// </summary>
    public static Task SealAsync(string indexPath, string workingPath, string masterPassword, byte[] salt)
    {
        SealFromWorkingFolder(indexPath, workingPath, masterPassword, salt);
        return Task.CompletedTask;
    }
}
