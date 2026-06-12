using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService;
    private readonly AppOptions _options;

    private RagQueryEngine? _engine;
    private string? _engineModelPath;
    private string? _engineIndexPath;

    [ObservableProperty]
    private string documentsFolder = string.Empty;

    [ObservableProperty]
    private string modelPath = string.Empty;

    [ObservableProperty]
    private string indexPath = string.Empty;

    [ObservableProperty]
    private int topK = 5;

    [ObservableProperty]
    private int contextSize = 4096;

    [ObservableProperty]
    private string questionText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<ChatEntry> Messages { get; } = new();

    public MainViewModel()
    {
        _configService = new ConfigService();
        _options = _configService.Load();

        documentsFolder = _options.DocumentsFolder;
        modelPath = _options.ModelPath;
        indexPath = _options.IndexPath;
        topK = _options.TopK;
        contextSize = _options.ContextSize;

        RefreshStatus();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta com seus documentos"
        };

        if (!string.IsNullOrWhiteSpace(DocumentsFolder) && Directory.Exists(DocumentsFolder))
        {
            dialog.InitialDirectory = DocumentsFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            DocumentsFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o modelo .gguf",
            Filter = "Modelos GGUF (*.gguf)|*.gguf|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ModelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task IndexAsync()
    {
        if (string.IsNullOrWhiteSpace(DocumentsFolder) || !Directory.Exists(DocumentsFolder))
        {
            StatusMessage = "Selecione uma pasta válida para indexar.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Indexando...";

        try
        {
            SaveCurrentOptions();

            var indexer = new DocumentIndexer();
            var progress = new Progress<string>(message => StatusMessage = message);
            var stats = await indexer.IndexFolderAsync(DocumentsFolder, IndexPath, _options.ChunkSize, _options.ChunkOverlap, progress);

            StatusMessage = $"Concluído: {stats.FilesProcessed} arquivos processados, " +
                            $"{stats.FilesSkipped} ignorados, {stats.ChunksIndexed} trechos indexados." +
                            (stats.Errors.Count > 0 ? $" ({stats.Errors.Count} erro(s))" : string.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao indexar: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        var question = QuestionText.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        Messages.Add(new ChatEntry { Role = "Você", Text = question });
        QuestionText = string.Empty;

        IsBusy = true;
        StatusMessage = "Pensando... (pode demorar na primeira pergunta, enquanto o modelo carrega)";

        try
        {
            SaveCurrentOptions();

            var search = new SearchService(IndexPath);
            if (!search.IndexExists())
            {
                Messages.Add(new ChatEntry { Role = "Erro", Text = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
                return;
            }

            var engine = GetEngine();
            var result = await engine.AskAsync(question, TopK);

            var sources = result.Sources.Count > 0
                ? $"Fontes: {string.Join(", ", result.Sources.Select(s => s.FileName).Distinct())}"
                : null;

            Messages.Add(new ChatEntry { Role = "QuestResume", Text = result.Answer, Sources = sources });
        }
        catch (ModelNotConfiguredException ex)
        {
            Messages.Add(new ChatEntry { Role = "Erro", Text = ex.Message });
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatEntry { Role = "Erro", Text = $"Erro: {ex.Message}" });
        }
        finally
        {
            StatusMessage = string.Empty;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveConfig()
    {
        SaveCurrentOptions();
        RefreshStatus();
    }

    private RagQueryEngine GetEngine()
    {
        if (_engine is null || _engineModelPath != ModelPath || _engineIndexPath != IndexPath)
        {
            _engine?.Dispose();

            var search = new SearchService(IndexPath);
            _engine = new RagQueryEngine(search, ModelPath, ContextSize, TopK);
            _engineModelPath = ModelPath;
            _engineIndexPath = IndexPath;
        }

        return _engine;
    }

    private void SaveCurrentOptions()
    {
        _options.DocumentsFolder = DocumentsFolder;
        _options.ModelPath = ModelPath;
        _options.IndexPath = IndexPath;
        _options.TopK = TopK;
        _options.ContextSize = ContextSize;
        _configService.Save(_options);
    }

    private void RefreshStatus()
    {
        var search = new SearchService(IndexPath);
        var indexExists = search.IndexExists();
        var modelConfigured = !string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath);
        var modelText = modelConfigured ? "modelo de IA pronto" : "modelo de IA não configurado";

        StatusMessage = indexExists
            ? $"{search.GetDocumentCount()} trechos indexados · {modelText}"
            : $"Sem índice ainda · {modelText}";
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
