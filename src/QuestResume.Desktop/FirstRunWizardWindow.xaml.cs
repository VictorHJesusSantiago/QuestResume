using System.IO;
using System.Windows;
using Microsoft.Win32;
using QuestResume.Core.Configuration;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;

namespace QuestResume.Desktop;

/// <summary>
/// Simple 3-step first-run wizard shown when <see cref="AppOptions.DocumentsFolder"/>,
/// <see cref="AppOptions.IndexPath"/> and <see cref="AppOptions.ModelPath"/> are all empty at
/// startup (see <see cref="App.Application_Startup"/>), mirroring the Web UI's onboarding flow.
/// Step 1 picks the documents folder, step 2 the local .gguf model, and step 3 optionally runs
/// the first indexing pass. Options are saved via the existing <see cref="ConfigService"/> when
/// the wizard is completed.
/// </summary>
public partial class FirstRunWizardWindow : Window
{
    private readonly AppOptions _options;
    private readonly ConfigService _configService;
    private int _step = 1;

    public FirstRunWizardWindow(AppOptions options, ConfigService configService)
    {
        InitializeComponent();
        _options = options;
        _configService = configService;

        DocumentsFolderBox.Text = options.DocumentsFolder;
        ModelPathBox.Text = options.ModelPath;

        // Index path is derived here (never asked explicitly) so the wizard stays to 3 steps as
        // requested; falls back to a sensible default next to the documents folder.
        if (string.IsNullOrWhiteSpace(_options.IndexPath))
        {
            _options.IndexPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuestResume", "index");
        }
    }

    private void OnBrowseDocumentsFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta com seus documentos" };
        if (dialog.ShowDialog() == true)
        {
            DocumentsFolderBox.Text = dialog.FolderName;
        }
    }

    private void OnBrowseModel(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o modelo .gguf",
            Filter = "Modelos GGUF (*.gguf)|*.gguf|Todos os arquivos (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ModelPathBox.Text = dialog.FileName;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_step <= 1) return;
        _step--;
        ShowStep(_step);
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_step < 3)
        {
            _step++;
            ShowStep(_step);
            return;
        }

        // Step 3: save options and, optionally, run the first indexing pass before closing.
        _options.DocumentsFolder = DocumentsFolderBox.Text.Trim();
        _options.ModelPath = ModelPathBox.Text.Trim();
        _configService.Save(_options);

        if (IndexNowCheckBox.IsChecked == true
            && !string.IsNullOrWhiteSpace(_options.DocumentsFolder)
            && Directory.Exists(_options.DocumentsFolder))
        {
            NextButton.IsEnabled = false;
            BackButton.IsEnabled = false;
            try
            {
                var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(_options), loadPlugins: true);
                var indexer = new DocumentIndexer(registry, null, null);
                var progress = new Progress<string>(message => Step3Status.Text = message);
                var stats = await indexer.IndexFolderAsync(
                    _options.DocumentsFolder, _options.IndexPath, _options.ChunkSize, _options.ChunkOverlap, progress,
                    maxFileSizeBytes: _options.MaxFileSizeBytes, excludedFolders: _options.ExcludedFolders,
                    piiRedactionEnabled: _options.PiiRedactionEnabled, parallelism: _options.IndexingParallelism,
                    incrementalIndexingEnabled: _options.IncrementalIndexingEnabled);

                Step3Status.Text = $"Concluído: {stats.FilesProcessed} arquivo(s) indexado(s).";
            }
            catch (Exception ex)
            {
                Step3Status.Text = $"Erro ao indexar: {ex.Message} (você pode indexar novamente na aba Perguntas).";
            }
        }

        DialogResult = true;
        Close();
    }

    private void ShowStep(int step)
    {
        Step1Panel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        TitleText.Text = step switch
        {
            1 => "Passo 1 de 3 — Pasta de documentos",
            2 => "Passo 2 de 3 — Modelo de IA",
            _ => "Passo 3 de 3 — Primeira indexação"
        };

        BackButton.IsEnabled = step > 1;
        NextButton.Content = step == 3 ? "Concluir" : "Próximo";
    }
}
