using QuestResume.Mobile.Services;

namespace QuestResume.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SessionService _session;

    public SettingsPage(SessionService session)
    {
        InitializeComponent();
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UsernameLabel.Text = _session.Username ?? "(desconhecido)";
        ServerUrlEntry.Text = _session.ServerUrl;
    }

    private async void OnSaveServerUrlClicked(object? sender, EventArgs e)
    {
        var url = ServerUrlEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            await DisplayAlert("Configurações", "Informe uma URL de servidor válida.", "OK");
            return;
        }

        _session.SetServerUrl(url);
        await DisplayAlert("Configurações", "URL do servidor atualizada.", "OK");
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _session.Logout();
        Application.Current!.Windows[0].Page = new NavigationPage(new LoginPage(
            App.CurrentApiClient, _session));
        await Task.CompletedTask;
    }
}
