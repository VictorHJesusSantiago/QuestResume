using System.Security.Cryptography;

namespace QuestResume.Core.Security;

/// <summary>
/// Criptografia simétrica AES-256-GCM de uso geral, usada tanto pela camada de criptografia em
/// nível de aplicação do <c>VectorStore</c> (texto e vetores de embedding) quanto pelo
/// empacotamento criptografado do índice Lucene (<see cref="LuceneIndexEncryptionService"/> na
/// pasta <c>Indexing</c>).
///
/// Por que não SQLCipher: SQLCipher exige uma build nativa alternativa da libsqlite3 (com
/// suporte a `PRAGMA key`) que não é a que o Microsoft.Data.Sqlite referencia por padrão,
/// obrigando a trocar de provedor de conexão e a distribuir binários nativos específicos por
/// plataforma/arquitetura — um risco real de quebrar o build em CI e nas máquinas dos usuários
/// (Windows/Linux/macOS, x64/arm64). Como o requisito é "dados não legíveis em texto claro sem a
/// senha" e não "todo o arquivo .db é opaco a nível de página", criptografar o conteúdo
/// (texto do chunk e bytes do embedding) em nível de aplicação com AES-GCM, antes de qualquer
/// gravação no SQLite, atinge o mesmo objetivo de confidencialidade sem dependências nativas
/// adicionais nem troca de provedor de banco.
/// </summary>
public static class AesCryptoHelper
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    /// <summary>
    /// Criptografa <paramref name="plaintext"/> com AES-256-GCM. O resultado é
    /// <c>nonce (12 bytes) || tag (16 bytes) || ciphertext</c>, para poder ser armazenado como um
    /// único blob.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
        return result;
    }

    public static byte[] Decrypt(byte[] payload, byte[] key)
    {
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Payload criptografado inválido ou corrompido.");
        }

        var nonce = payload.AsSpan(0, NonceSizeBytes).ToArray();
        var tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes).ToArray();
        var ciphertext = payload.AsSpan(NonceSizeBytes + TagSizeBytes).ToArray();
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static byte[] EncryptString(string plaintext, byte[] key)
        => Encrypt(System.Text.Encoding.UTF8.GetBytes(plaintext), key);

    public static string DecryptString(byte[] payload, byte[] key)
        => System.Text.Encoding.UTF8.GetString(Decrypt(payload, key));

    /// <summary>Criptografa um arquivo inteiro (usado para empacotar o índice Lucene em um único .enc).</summary>
    public static void EncryptFile(string sourcePath, string destinationPath, byte[] key)
    {
        var plaintext = File.ReadAllBytes(sourcePath);
        File.WriteAllBytes(destinationPath, Encrypt(plaintext, key));
    }

    public static void DecryptFile(string sourcePath, string destinationPath, byte[] key)
    {
        var payload = File.ReadAllBytes(sourcePath);
        File.WriteAllBytes(destinationPath, Decrypt(payload, key));
    }
}
