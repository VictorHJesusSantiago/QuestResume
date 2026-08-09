using System.Security.Cryptography;

namespace QuestResume.Core.Security;

public static class AesCryptoHelper
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

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
