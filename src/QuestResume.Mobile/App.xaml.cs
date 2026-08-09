using Microsoft.Extensions.DependencyInjection;
using QuestResume.Mobile.Pages;
using QuestResume.Mobile.Services;

namespace QuestResume.Mobile;

public partial class App : Application
{
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
