using System.Security.Cryptography;
using System.Text;

namespace QuestResume.Core.Auth;

public static class TotpService
{
    private const int PeriodSeconds = 30;
    private const int Digits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

        public static string GenerateCode(string base32Secret, DateTimeOffset time)
    {
        var counter = time.ToUnixTimeSeconds() / PeriodSeconds;
        return GenerateCodeForCounter(base32Secret, counter);
    }

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
                continue; 
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
