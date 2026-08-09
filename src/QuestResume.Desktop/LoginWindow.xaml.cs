using System.Windows;
using QuestResume.Core.Auth;

namespace QuestResume.Desktop;

public partial class LoginWindow : Window
{
    private readonly UserStore _userStore;

    public User? AuthenticatedUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        _userStore = new UserStore();
    }

    private void OnLoginClick(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text;
        var password = PasswordBox.Password;

        var user = _userStore.ValidateCredentials(username, password);
        if (user is null)
        {
            ErrorText.Text = "Usuário ou senha inválidos.";
            return;
        }

        AuthenticatedUser = user;
        DialogResult = true;
        Close();
    }
}
