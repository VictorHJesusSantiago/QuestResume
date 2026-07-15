using QuestResume.Mobile.Services;

namespace QuestResume.Mobile.Pages;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient;
    private readonly SessionService _session;

    public LoginPage(ApiClient apiClient, SessionService session)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrWhiteSpace(ServerUrlEntry.Text))
        {
            ServerUrlEntry.Text = _session.ServerUrl;
        }
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;

        var serverUrl = ServerUrlEntry.Text?.Trim() ?? string.Empty;
        var username = UsernameEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ErrorLabel.Text = "Preencha a URL do servidor, usuário e senha.";
            ErrorLabel.IsVisible = true;
            return;
        }

        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        LoginButton.IsEnabled = false;

        try
        {
            var result = await _apiClient.LoginAsync(serverUrl, username, password);
            await _session.SaveLoginAsync(serverUrl, result.Token, result.Username);

            Application.Current!.Windows[0].Page = new AppShell();
        }
        catch (ApiException ex)
        {
            ErrorLabel.Text = ex.Message;
            ErrorLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"Falha ao conectar ao servidor: {ex.Message}";
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            LoginButton.IsEnabled = true;
        }
    }
}
