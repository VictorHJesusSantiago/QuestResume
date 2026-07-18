using System.Security.Cryptography;
using QuestResume.Core.Auth;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Models;
using QuestResume.Core.Persistence;
using Xunit;

namespace QuestResume.Core.Tests;

public class TotpServiceTests
{
    [Fact]
    public void GenerateSecret_ProducesValidBase32()
    {
        var secret = TotpService.GenerateSecret();
        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void GeneratedCode_ValidatesForSameWindow()
    {
        var secret = TotpService.GenerateSecret();
        var now = DateTimeOffset.UtcNow;
        var code = TotpService.GenerateCode(secret, now);
        Assert.Equal(6, code.Length);
        Assert.True(TotpService.ValidateCode(secret, code, now));
    }

    [Fact]
    public void ValidateCode_RejectsWrongCode()
    {
        var secret = TotpService.GenerateSecret();
        Assert.False(TotpService.ValidateCode(secret, "000000", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void KnownVector_Rfc6238_Sha1()
    {
        // Segredo ASCII "12345678901234567890" -> Base32. Vetor de teste RFC 6238 para T=59s.
        const string base32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var time = DateTimeOffset.FromUnixTimeSeconds(59);
        var code = TotpService.GenerateCode(base32, time);
        Assert.Equal("287082", code);
    }

    [Fact]
    public void BuildOtpAuthUri_ContainsSecret()
    {
        var uri = TotpService.BuildOtpAuthUri("QuestResume", "alice", "ABC234");
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=ABC234", uri);
    }
}

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("short1A")]      // < 8
    [InlineData("lowercase1")]   // sem maiúscula
    [InlineData("NoDigitsHere")] // sem número
    public void ValidatePasswordStrength_RejectsWeak(string password)
    {
        Assert.Throws<InvalidOperationException>(() => UserStore.ValidatePasswordStrength(password));
    }

    [Fact]
    public void ValidatePasswordStrength_AcceptsStrong()
    {
        UserStore.ValidatePasswordStrength("Senha1234");
    }

    [Fact]
    public void CreateUser_RejectsWeakPassword()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-users-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserStore(path);
            Assert.Throws<InvalidOperationException>(() => store.CreateUser("bob", "weak", UserRole.User));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnableTotp_PersistsSecret()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-users-{Guid.NewGuid():N}.json");
        try
        {
            var store = new UserStore(path);
            store.CreateUser("carol", "Senha1234", UserRole.User);
            var secret = store.EnableTotp("carol");
            Assert.False(string.IsNullOrWhiteSpace(secret));
            var user = store.FindByUsername("carol");
            Assert.NotNull(user);
            Assert.True(user!.TotpEnabled);
            Assert.Equal(secret, user.TotpSecret);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ExpandedPiiRedactorTests
{
    [Fact]
    public void Redact_MasksRg()
    {
        Assert.Contains("[RG]", PiiRedactor.Redact("RG: 12.345.678-9"));
    }

    [Fact]
    public void Redact_MasksPisPasep()
    {
        Assert.Contains("[PIS_PASEP]", PiiRedactor.Redact("PIS 123.45678.90-1"));
    }

    [Fact]
    public void Redact_MasksPixUuidKey()
    {
        Assert.Contains("[CHAVE_PIX]", PiiRedactor.Redact("chave 123e4567-e89b-42d3-a456-426614174000"));
    }

    [Fact]
    public void Redact_MasksIban()
    {
        Assert.Contains("[IBAN]", PiiRedactor.Redact("conta BR9700360305000010009795493P1"));
    }
}

public class ReversibleAnonymizerTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Anonymize_ReplacesAndRoundTrips()
    {
        var key = Key();
        var text = "Contato: joao@example.com e maria@example.com, novamente joao@example.com.";
        var result = ReversibleAnonymizer.Anonymize(text, key);

        Assert.DoesNotContain("joao@example.com", result.AnonymizedText);
        Assert.Contains("[EMAIL_1]", result.AnonymizedText);
        Assert.Equal(2, result.ReplacedCount); // dois emails distintos

        var restored = ReversibleAnonymizer.Deanonymize(result.AnonymizedText, result.EncryptedMap, key);
        Assert.Equal(text, restored);
    }

    [Fact]
    public void Deanonymize_WithWrongKey_Fails()
    {
        var result = ReversibleAnonymizer.Anonymize("email a@b.com", Key());
        Assert.ThrowsAny<CryptographicException>(
            () => ReversibleAnonymizer.Deanonymize(result.AnonymizedText, result.EncryptedMap, Key()));
    }
}

public class VectorQuantizerTests
{
    [Fact]
    public void QuantizeDequantize_ApproximatesOriginal()
    {
        var vector = new[] { 0.1f, -0.5f, 0.9f, 0.0f, -0.3f };
        var (q, scale) = VectorQuantizer.Quantize(vector);
        var back = VectorQuantizer.Dequantize(q, scale);
        for (var i = 0; i < vector.Length; i++)
        {
            Assert.True(Math.Abs(vector[i] - back[i]) <= scale, $"idx {i}");
        }
    }

    [Fact]
    public void RoundTripBytes_DetectedAndRestored()
    {
        var vector = new[] { 0.2f, 0.8f, -0.4f };
        var blob = VectorQuantizer.ToQuantizedBytes(vector);
        Assert.True(VectorQuantizer.IsQuantized(blob));
        var back = VectorQuantizer.FromQuantizedBytes(blob);
        Assert.Equal(vector.Length, back.Length);
    }

    [Fact]
    public void Float32Blob_NotDetectedAsQuantized()
    {
        var vector = new[] { 1.0f, 2.0f, 3.0f };
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        Assert.False(VectorQuantizer.IsQuantized(bytes));
    }
}

public class SecureWipeServiceTests
{
    [Fact]
    public void WipeFile_DeletesFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"qr-wipe-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(4096));
        SecureWipeService.WipeFile(path, passes: 2);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void WipeDirectory_RemovesTree()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"qr-wipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(dir, "sub", "b.txt"), "world");
        var count = SecureWipeService.WipeDirectory(dir, passes: 1);
        Assert.Equal(2, count);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void WipeFile_MissingFile_NoThrow()
    {
        SecureWipeService.WipeFile(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));
    }
}

public class CachingEmbeddingServiceTests
{
    private sealed class CountingEmbeddingService : IEmbeddingService
    {
        public int Calls;
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new[] { (float)text.Length });
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task RepeatedQuery_HitsCache()
    {
        var inner = new CountingEmbeddingService();
        using var cache = new CachingEmbeddingService(inner, maxEntries: 10);

        await cache.EmbedAsync("Olá Mundo");
        await cache.EmbedAsync("olá mundo"); // normalizado -> mesma chave
        await cache.EmbedAsync("  OLÁ MUNDO  ");

        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Lru_EvictsOldest()
    {
        var inner = new CountingEmbeddingService();
        using var cache = new CachingEmbeddingService(inner, maxEntries: 2);
        await cache.EmbedAsync("a");
        await cache.EmbedAsync("b");
        await cache.EmbedAsync("c");
        Assert.Equal(2, cache.Count);
    }
}
