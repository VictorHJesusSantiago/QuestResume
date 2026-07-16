using Microsoft.Maui.Storage;

namespace QuestResume.Mobile.Services;

/// <summary>
/// Guarda a URL do servidor e o token JWT da sessão atual. O token é persistido com
/// <see cref="SecureStorage"/> (Keystore no Android / Keychain no iOS); a URL do servidor é
/// persistida com <see cref="Preferences"/> (não é segredo).
/// </summary>
public sealed class SessionService
{
    private const string TokenKey = "questresume_token";
    private const string UsernameKey = "questresume_username";
    private const string ServerUrlKey = "questresume_server_url";

    public string? Token { get; private set; }
    public string? Username { get; private set; }
    public string ServerUrl { get; private set; } = string.Empty;

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Token);

    public async Task LoadAsync()
    {
        ServerUrl = Preferences.Default.Get(ServerUrlKey, string.Empty);
        try
        {
            Token = await SecureStorage.Default.GetAsync(TokenKey);
            Username = await SecureStorage.Default.GetAsync(UsernameKey);
        }
        catch
        {
            // SecureStorage pode falhar em alguns emuladores sem keystore configurado —
            // trata como "sem sessão salva" em vez de derrubar o app.
            Token = null;
            Username = null;
        }
    }

    public async Task SaveLoginAsync(string serverUrl, string token, string username)
    {
        ServerUrl = serverUrl;
        Token = token;
        Username = username;
        Preferences.Default.Set(ServerUrlKey, serverUrl);
        await SecureStorage.Default.SetAsync(TokenKey, token);
        await SecureStorage.Default.SetAsync(UsernameKey, username);
    }

    public void SetServerUrl(string serverUrl)
    {
        ServerUrl = serverUrl;
        Preferences.Default.Set(ServerUrlKey, serverUrl);
    }

    public void Logout()
    {
        Token = null;
        Username = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(UsernameKey);
    }
}
