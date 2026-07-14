using System.Windows;
using QuestResume.Core.Auth;

namespace QuestResume.Desktop;

/// <summary>
/// Tela de login exibida antes da janela principal quando existem usuários cadastrados
/// (<see cref="UserStore"/>). A sessão autenticada é mantida apenas em memória (não há token
/// persistido em disco pelo Desktop) enquanto a aplicação estiver aberta.
/// </summary>
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
