using System.Collections.Concurrent;

namespace QuestResume.Core.CloudSync;

/// <summary>
/// Guarda em memória, no servidor, o <c>code_verifier</c> PKCE e o <c>redirect_uri</c>
/// associados a um valor opaco de <c>state</c> gerado ao iniciar o fluxo OAuth2 (endpoint
/// <c>GET /api/cloud/{provider}/auth-url</c>).
/// </summary>
/// <remarks>
/// O redirecionamento que o provedor de nuvem (Google/OneDrive/Dropbox) faz de volta para
/// <c>GET /api/cloud/{provider}/callback</c> só inclui os parâmetros padrão OAuth2 (<c>code</c>
/// e o <c>state</c> que enviamos na URL de autorização) — o navegador nunca anexa parâmetros
/// arbitrários como <c>codeVerifier</c>/<c>redirectUri</c> por conta própria. Por isso o
/// <c>code_verifier</c> e o <c>redirect_uri</c> usados para construir a URL de autorização
/// precisam ser recuperados no servidor a partir do <c>state</c>, em vez de serem
/// reenviados pelo cliente (que nunca teria como fazê-lo, já que quem navega para o
/// callback é o navegador seguindo o redirecionamento do provedor, não o JavaScript da página).
/// Entradas expiram após 10 minutos e são removidas na primeira consulta (uso único).
/// </remarks>
public static class CloudOAuthStateStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private sealed record Entry(string Provider, string CodeVerifier, string RedirectUri, DateTimeOffset ExpiresAtUtc);

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();

    public static string Save(string provider, string codeVerifier, string redirectUri)
    {
        var state = Guid.NewGuid().ToString("N");
        Entries[state] = new Entry(provider, codeVerifier, redirectUri, DateTimeOffset.UtcNow.Add(Ttl));
        PruneExpired();
        return state;
    }

    /// <summary>
    /// Recupera e remove (uso único) a entrada associada a <paramref name="state"/>, validando
    /// que ainda não expirou e que pertence ao provedor esperado.
    /// </summary>
    public static bool TryConsume(string state, string expectedProvider, out string codeVerifier, out string redirectUri)
    {
        codeVerifier = string.Empty;
        redirectUri = string.Empty;

        if (string.IsNullOrWhiteSpace(state) || !Entries.TryRemove(state, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAtUtc < DateTimeOffset.UtcNow ||
            !string.Equals(entry.Provider, expectedProvider, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        codeVerifier = entry.CodeVerifier;
        redirectUri = entry.RedirectUri;
        return true;
    }

    private static void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in Entries)
        {
            if (kv.Value.ExpiresAtUtc < now)
            {
                Entries.TryRemove(kv.Key, out _);
            }
        }
    }
}
