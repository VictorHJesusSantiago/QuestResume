using System.Windows;
using QuestResume.Core.Configuration;
using QuestResume.Core.Security;

namespace QuestResume.Desktop;

public partial class MasterKeyWindow : Window
{
    private readonly AppOptions _options;

    public bool Unlocked { get; private set; }

    public MasterKeyWindow(AppOptions options)
    {
        InitializeComponent();
        _options = options;
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(password) || !MasterKeyManager.VerifyPassword(password, _options.MasterKeyVerifier))
        {
            ErrorText.Text = "Senha incorreta. Tente novamente.";
            PasswordBox.Clear();
            PasswordBox.Focus();
            return;
        }

        MasterKeySession.MasterPassword = password;
        Unlocked = true;
        DialogResult = true;
        Close();
    }
}
