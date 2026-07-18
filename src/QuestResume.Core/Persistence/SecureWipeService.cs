using System.Security.Cryptography;

namespace QuestResume.Core.Persistence;

/// <summary>
/// Apagamento "seguro" de arquivos do índice/vetores: sobrescreve os bytes do arquivo com dados
/// aleatórios em várias passadas antes de deletá-lo, dificultando a recuperação forense simples.
///
/// LIMITAÇÃO IMPORTANTE (honestidade técnica): em SSDs modernos, memória flash e sistemas de
/// arquivos com copy-on-write (APFS, Btrfs, ZFS), journaling ou wear-leveling, sobrescrever um
/// arquivo NÃO garante que os blocos físicos originais tenham sido de fato regravados — o
/// controlador pode redirecionar a escrita para outras células, deixando cópias do dado antigo
/// fisicamente presentes. Isto é, portanto, uma MITIGAÇÃO (defense-in-depth) e NÃO uma garantia
/// criptográfica de destruição. Para garantia real, use criptografia em repouso
/// (<see cref="QuestResume.Core.Configuration.AppOptions.EncryptionEnabled"/>) e destrua a chave,
/// ou o apagamento seguro nativo do dispositivo (ATA Secure Erase / criptografia total do disco).
/// </summary>
public static class SecureWipeService
{
    /// <summary>Número padrão de passadas de sobrescrita.</summary>
    public const int DefaultPasses = 3;

    /// <summary>
    /// Sobrescreve <paramref name="filePath"/> com bytes aleatórios em <paramref name="passes"/>
    /// passadas e então o remove. Sem efeito (silencioso) se o arquivo não existir.
    /// </summary>
    public static void WipeFile(string filePath, int passes = DefaultPasses)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        var length = new FileInfo(filePath).Length;
        if (length > 0)
        {
            const int bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];

            for (var pass = 0; pass < Math.Max(1, passes); pass++)
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
                long written = 0;
                while (written < length)
                {
                    var chunk = (int)Math.Min(bufferSize, length - written);
                    RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                    stream.Write(buffer, 0, chunk);
                    written += chunk;
                }

                stream.Flush(flushToDisk: true);
            }
        }

        File.Delete(filePath);
    }

    /// <summary>
    /// Aplica <see cref="WipeFile"/> recursivamente a todos os arquivos de <paramref name="directoryPath"/>
    /// e então remove a árvore de diretórios. Sem efeito se o diretório não existir. Retorna o
    /// número de arquivos apagados de forma segura.
    /// </summary>
    public static int WipeDirectory(string directoryPath, int passes = DefaultPasses)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            WipeFile(file, passes);
            count++;
        }

        Directory.Delete(directoryPath, recursive: true);
        return count;
    }
}
