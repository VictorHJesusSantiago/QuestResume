namespace QuestResume.Desktop;

/// <summary>
/// In-memory-only holder for the master password entered in <see cref="MasterKeyWindow"/>, for
/// the lifetime of the Desktop process. Never persisted to disk and never logged — mirrors how
/// <see cref="LoginWindow"/> hands its authenticated user to the rest of the app purely via an
/// in-memory property rather than any kind of on-disk token cache.
/// </summary>
public static class MasterKeySession
{
    public static string? MasterPassword { get; set; }
}
