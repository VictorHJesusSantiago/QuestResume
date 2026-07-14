using System.Windows;
using QuestResume.Core.Configuration;
using QuestResume.Core.Security;

namespace QuestResume.Desktop;

/// <summary>
/// Prompts for the master password when <see cref="AppOptions.EncryptionEnabled"/> is true,
/// shown right after a successful <see cref="LoginWindow"/> (or immediately at startup in
/// single-user/no-auth mode) and before <see cref="MainWindow"/> is created. The password is
/// validated against the persisted PBKDF2 verifier (<see cref="AppOptions.MasterKeyVerifier"/>)
/// via <see cref="MasterKeyManager.VerifyPassword"/> — it is never logged and never written to
/// disk; on success it is only kept in memory via <see cref="MasterKeySession"/> for the
/// lifetime of the process.
/// </summary>
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
