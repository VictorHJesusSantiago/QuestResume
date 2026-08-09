using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using QuestResume.Core.Auth;
using QuestResume.Core.Configuration;
using QuestResume.Desktop.ViewModels;

namespace QuestResume.Desktop;

public partial class MainWindow : Window
{
    
    
    
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
            return; 
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

        private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedFolder(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

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
