using Microsoft.Maui.Storage;

namespace QuestResume.Mobile.Services;

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
