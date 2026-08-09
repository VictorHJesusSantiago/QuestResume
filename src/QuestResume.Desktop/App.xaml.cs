using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;

namespace QuestResume.Desktop;

public partial class App : Application
{
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

