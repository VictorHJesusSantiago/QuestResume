using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;

namespace QuestResume.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// When at least one user is registered (Core/Auth/UserStore), requires a successful login
    /// via <see cref="LoginWindow"/> before opening <see cref="MainWindow"/>. With no users
    /// registered (default/single-user mode), the main window opens directly — unchanged
    /// behavior for existing installs that have never used "user add". Afterwards, if
    /// <see cref="AppOptions.EncryptionEnabled"/> is also set, <see cref="MasterKeyWindow"/> is
    /// shown to unlock the encrypted index before <see cref="MainWindow"/> is created. Cancelling
    /// or closing either window shuts the app down cleanly without opening the main window.
    /// </summary>
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var options = new ConfigService().Load();
        ApplyUiLanguage(options.UiLanguage);

        var userStore = new UserStore();
        if (userStore.HasAnyUser())
        {
            var login = new LoginWindow();
            var loggedIn = login.ShowDialog();
            if (loggedIn != true)
            {
                Shutdown();
                return;
            }
        }

        if (options.EncryptionEnabled)
        {
            var masterKeyWindow = new MasterKeyWindow(options);
            var unlocked = masterKeyWindow.ShowDialog();
            if (unlocked != true)
            {
                Shutdown();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(options.DocumentsFolder)
            && string.IsNullOrWhiteSpace(options.IndexPath)
            && string.IsNullOrWhiteSpace(options.ModelPath))
        {
            var wizard = new FirstRunWizardWindow(options, new ConfigService());
            wizard.ShowDialog();
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// Replaces the currently merged language <see cref="ResourceDictionary"/> (if any) with the
    /// one matching <paramref name="uiLanguage"/> ("pt-BR" or "en-US", defaulting to pt-BR for
    /// unknown/empty values). Because <c>MainWindow.xaml</c> binds its texts via
    /// <c>{DynamicResource}</c>, replacing the dictionary in
    /// <see cref="ResourceDictionary.MergedDictionaries"/> updates the UI immediately, without
    /// restarting the app — used both at startup and when the user changes the language in
    /// Configurações.
    /// </summary>
    public static void ApplyUiLanguage(string? uiLanguage)
    {
        var fileName = uiLanguage == "en-US" ? "Strings.en-US.xaml" : "Strings.pt-BR.xaml";
        var uri = new Uri($"Resources/{fileName}", UriKind.Relative);
        var newDictionary = new ResourceDictionary { Source = uri };

        var dictionaries = Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = newDictionary;
        }
        else
        {
            dictionaries.Add(newDictionary);
        }
    }
}

