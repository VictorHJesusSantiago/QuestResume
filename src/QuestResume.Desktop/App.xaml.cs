using System.Configuration;
using System.Data;
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

        var options = new ConfigService().Load();
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

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }
}

