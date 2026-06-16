using System.IO;
using System.Linq;
using System.Windows;
using QuestResume.Desktop.ViewModels;

namespace QuestResume.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var viewModel = new MainViewModel();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
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
