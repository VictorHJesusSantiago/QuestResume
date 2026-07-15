using System.Security.Cryptography;
using System.Text;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Gera o par (code_verifier, code_challenge) do PKCE (RFC 7636), usado pelo fluxo
/// "Authorization Code with PKCE" nos provedores de nuvem — evita a necessidade de um
/// client secret para aplicativos desktop/CLI públicos.
/// </summary>
public static class PkceHelper
{
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    public static string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
