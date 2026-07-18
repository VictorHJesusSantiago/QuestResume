using System.Security.Cryptography;
using System.Text;

namespace QuestResume.Core.Auth;

/// <summary>
/// Implementação local de TOTP (Time-based One-Time Password, RFC 6238) usando apenas
/// <see cref="System.Security.Cryptography"/> — sem bibliotecas externas. Gera segredos em Base32
/// (RFC 4648) compatíveis com apps autenticadores (Google Authenticator, Aegis, etc.), calcula o
/// código de 6 dígitos a partir do tempo Unix dividido em janelas de 30s (HMAC-SHA1) e valida um
/// código informado com tolerância de ±1 janela para compensar diferenças de relógio.
/// </summary>
public static class TotpService
{
    private const int PeriodSeconds = 30;
    private const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Gera um segredo TOTP aleatório (20 bytes = 160 bits) codificado em Base32.</summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    /// <summary>
    /// Calcula o código TOTP de 6 dígitos para o instante informado (UTC), a partir do segredo
    /// Base32. Usado tanto na geração quanto na validação.
    /// </summary>
    public static string GenerateCode(string base32Secret, DateTimeOffset time)
    {
        var counter = time.ToUnixTimeSeconds() / PeriodSeconds;
        return GenerateCodeForCounter(base32Secret, counter);
    }

    /// <summary>
    /// Valida <paramref name="code"/> contra o segredo, aceitando a janela atual e ±1 janela
    /// (tolerância a desvio de relógio). Retorna true se qualquer uma bater.
    /// </summary>
    public static bool ValidateCode(string base32Secret, string code, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim();
        var time = now ?? DateTimeOffset.UtcNow;
        var counter = time.ToUnixTimeSeconds() / PeriodSeconds;

        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = GenerateCodeForCounter(base32Secret, counter + offset);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Monta a URI <c>otpauth://</c> que pode ser colada num app autenticador (ou virar QR code).
    /// </summary>
    public static string BuildOtpAuthUri(string issuer, string accountName, string base32Secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        var enc = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={enc}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    private static string GenerateCodeForCounter(string base32Secret, long counter)
    {
        var key = Base32Decode(base32Secret);
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);

        // Truncamento dinâmico (RFC 4226 seção 5.3).
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | ((hash[offset + 1] & 0xFF) << 16)
                     | ((hash[offset + 2] & 0xFF) << 8)
                     | (hash[offset + 3] & 0xFF);

        var otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString().PadLeft(Digits, '0');
    }

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                var index = (buffer >> (bitsLeft - 5)) & 0x1F;
                bitsLeft -= 5;
                sb.Append(Base32Alphabet[index]);
            }
        }

        if (bitsLeft > 0)
        {
            var index = (buffer << (5 - bitsLeft)) & 0x1F;
            sb.Append(Base32Alphabet[index]);
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string base32)
    {
        base32 = base32.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(base32.Length * 5 / 8);
        int buffer = 0, bitsLeft = 0;
        foreach (var c in base32)
        {
            var index = Base32Alphabet.IndexOf(c);
            if (index < 0)
            {
                continue; // ignora espaços/caracteres inválidos
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
