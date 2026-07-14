using System.Security.Cryptography;

namespace QuestResume.Core.Security;

/// <summary>
/// Deriva chaves AES a partir de uma senha mestre via PBKDF2 (<see cref="Rfc2898DeriveBytes"/>)
/// e gera/valida um "verificador" (sal + hash) que pode ser persistido em <c>config.json</c> em
/// vez da senha em si — a senha mestre nunca é gravada em disco em nenhuma forma reversível.
/// </summary>
public static class MasterKeyManager
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Prf = HashAlgorithmName.SHA256;

    /// <summary>
    /// Cria um verificador no formato <c>"{iterações}.{saltBase64}.{hashBase64}"</c>, seguro para
    /// persistir em texto claro no arquivo de configuração (não permite recuperar a senha).
    /// </summary>
    public static string CreateVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Prf, HashSizeBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    /// <summary>Verifica se <paramref name="password"/> corresponde ao verificador previamente criado.</summary>
    public static bool VerifyPassword(string password, string verifier)
    {
        var parts = verifier.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[1]);
        var expectedHash = Convert.FromBase64String(parts[2]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Prf, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    /// <summary>
    /// Deriva uma chave AES-256 de 32 bytes a partir da senha mestre e de um sal específico do
    /// recurso protegido (ex.: caminho do índice), para que a mesma senha produza chaves
    /// diferentes por índice.
    /// </summary>
    public static byte[] DeriveKey(string password, byte[] salt, int keySizeBytes = 32)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Prf, keySizeBytes);

    /// <summary>
    /// Extrai o sal embutido em um verificador criado por <see cref="CreateVerifier"/> (formato
    /// <c>"{iterações}.{saltBase64}.{hashBase64}"</c>), para ser reaproveitado como sal de
    /// <see cref="DeriveKey"/> ao criptografar recursos ligados a essa senha mestre (ex.: o
    /// índice Lucene em <see cref="QuestResume.Core.Indexing.LuceneIndexEncryptionService"/>),
    /// sem precisar persistir um sal separado em <c>config.json</c>.
    /// </summary>
    /// <exception cref="ArgumentException">Quando <paramref name="verifier"/> tem formato inválido.</exception>
    public static byte[] ExtractSalt(string verifier)
    {
        var parts = verifier.Split('.');
        if (parts.Length != 3)
        {
            throw new ArgumentException("Verificador de senha mestre em formato inválido.", nameof(verifier));
        }

        return Convert.FromBase64String(parts[1]);
    }
}
