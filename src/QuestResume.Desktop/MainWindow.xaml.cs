using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;
using QuestResume.Desktop.ViewModels;

namespace QuestResume.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // Bloqueio automático por inatividade (item 1): após este período sem interação do usuário
    // (mouse/teclado), a janela é escondida e a senha (mestre, se criptografia habilitada, ou de
    // usuário, se multiusuário) é reexigida via os mesmos diálogos usados na inicialização.
    private const int AutoLockMinutes = 15;
    private readonly DispatcherTimer _autoLockTimer;
    private bool _locking;

    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();

        _autoLockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(AutoLockMinutes)
        };
        _autoLockTimer.Tick += (_, _) => LockSession();
        _autoLockTimer.Start();

        // Rearma o timer a cada interação do usuário.
        PreviewMouseMove += (_, _) => ResetAutoLockTimer();
        PreviewMouseDown += (_, _) => ResetAutoLockTimer();
        PreviewKeyDown += (_, _) => ResetAutoLockTimer();
        Closed += (_, _) => _autoLockTimer.Stop();
    }

    private void ResetAutoLockTimer()
    {
        _autoLockTimer.Stop();
        _autoLockTimer.Start();
    }

    /// <summary>
    /// Esconde a janela principal e reexige a senha. Reutiliza <see cref="MasterKeyWindow"/> quando
    /// a criptografia está habilitada, senão <see cref="LoginWindow"/> quando há usuários
    /// cadastrados. Sem criptografia e sem usuários (modo single-user), não há o que reexigir.
    /// </summary>
    private void LockSession()
    {
        if (_locking)
        {
            return;
        }

        var options = new ConfigService().Load();
        var userStore = new UserStore();
        var needsLogin = userStore.HasAnyUser();
        var needsMasterKey = options.EncryptionEnabled;
        if (!needsLogin && !needsMasterKey)
        {
            return; // nada a bloquear
        }

        _locking = true;
        _autoLockTimer.Stop();
        try
        {
            Hide();

            if (needsMasterKey)
            {
                var win = new MasterKeyWindow(options);
                if (win.ShowDialog() != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }
            else
            {
                var win = new LoginWindow();
                if (win.ShowDialog() != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            Show();
            Activate();
        }
        finally
        {
            _locking = false;
            _autoLockTimer.Start();
        }
    }

    /// <summary>
    /// Shows a "copy" cursor while dragging a folder over the window, so the user gets
    /// feedback that dropping it will set the documents folder to index.
    /// </summary>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedFolder(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Dropping a folder anywhere on the window sets it as the documents folder to index,
    /// mirroring the "Procurar..." folder picker.
    /// </summary>
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedFolder(e, out var folderPath) && DataContext is MainViewModel viewModel)
        {
            viewModel.DocumentsFolder = folderPath;
        }
    }

    private static bool TryGetDroppedFolder(DragEventArgs e, out string folderPath)
    {
        folderPath = string.Empty;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        var folder = paths?.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            return false;
        }

        folderPath = folder;
        return true;
    }
}
