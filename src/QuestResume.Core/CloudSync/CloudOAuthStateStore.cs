using System.Collections.Concurrent;

namespace QuestResume.Core.CloudSync;

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
