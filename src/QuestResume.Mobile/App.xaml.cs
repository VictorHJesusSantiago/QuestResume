using Microsoft.Extensions.DependencyInjection;
using QuestResume.Mobile.Pages;
using QuestResume.Mobile.Services;

namespace QuestResume.Mobile;

public partial class App : Application
{
    /// <summary>
    /// Provedor de serviços da aplicação, exposto estaticamente para que páginas criadas
    /// manualmente fora do fluxo de navegação do Shell (ex.: troca de página após login/logout)
    /// também consigam resolver <see cref="ApiClient"/>/<see cref="SessionService"/> via DI.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public static ApiClient CurrentApiClient => Services.GetRequiredService<ApiClient>();

    public static SessionService CurrentSession => Services.GetRequiredService<SessionService>();

    public App(IServiceProvider services)
    {
        InitializeComponent();
        Services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var session = CurrentSession;
        var window = new Window(new ContentPage { Content = new ActivityIndicator { IsRunning = true, VerticalOptions = LayoutOptions.Center } });

        // Carrega sessão salva (URL do servidor + token) antes de decidir a página inicial.
        _ = InitializeStartPageAsync(window, session);

        return window;
    }

    private static async Task InitializeStartPageAsync(Window window, SessionService session)
    {
        await session.LoadAsync();
        window.Page = session.IsLoggedIn
            ? new AppShell()
            : new NavigationPage(new LoginPage(CurrentApiClient, session));
    }
}
