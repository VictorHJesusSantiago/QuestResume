using System.Security.Cryptography;

namespace QuestResume.Core.Security;

public static class MasterKeyManager
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 210_000;
    private static readonly HashAlgorithmName Prf = HashAlgorithmName.SHA256;

        public static string CreateVerifier(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Prf, HashSizeBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

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

        public static byte[] DeriveKey(string password, byte[] salt, int keySizeBytes = 32)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Prf, keySizeBytes);

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
