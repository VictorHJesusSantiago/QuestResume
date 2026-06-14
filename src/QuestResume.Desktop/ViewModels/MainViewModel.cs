using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService;
    private readonly AppOptions _options;

    private RagQueryEngine? _engine;
    private RagEngineKey? _engineKey;

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
    private string llmProvider = "LlamaSharp";

    [ObservableProperty]
    private string ollamaBaseUrl = "http://localhost:11434";

    [ObservableProperty]
    private string ollamaModel = "llama3.2";

    [ObservableProperty]
    private bool ocrEnabled;

    [ObservableProperty]
    private string tessDataPath = string.Empty;

    [ObservableProperty]
    private string ocrLanguages = "por+eng";

    [ObservableProperty]
    private bool embeddingsEnabled;

    [ObservableProperty]
    private string embeddingModelPath = string.Empty;

    [ObservableProperty]
    private string embeddingTokenizerPath = string.Empty;

    [ObservableProperty]
    private double hybridBm25Weight = 0.5;

    [ObservableProperty]
    private bool sttEnabled;

    [ObservableProperty]
    private string whisperModelPath = string.Empty;

    [ObservableProperty]
    private string questionText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<ChatEntry> Messages { get; } = new();

    public string[] LlmProviderOptions { get; } = { "LlamaSharp", "Ollama" };

    public MainViewModel()
    {
        _configService = new ConfigService();
        _options = _configService.Load();

        documentsFolder = _options.DocumentsFolder;
        modelPath = _options.ModelPath;
        indexPath = _options.IndexPath;
        topK = _options.TopK;
        contextSize = _options.ContextSize;
        llmProvider = _options.LlmProvider;
        ollamaBaseUrl = _options.OllamaBaseUrl;
        ollamaModel = _options.OllamaModel;
        ocrEnabled = _options.OcrEnabled;
        tessDataPath = _options.TessDataPath;
        ocrLanguages = _options.OcrLanguages;
        embeddingsEnabled = _options.EmbeddingsEnabled;
        embeddingModelPath = _options.EmbeddingModelPath;
        embeddingTokenizerPath = _options.EmbeddingTokenizerPath;
        hybridBm25Weight = _options.HybridBm25Weight;
        sttEnabled = _options.SttEnabled;
        whisperModelPath = _options.WhisperModelPath;

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
    private void BrowseIndexPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta do índice"
        };

        if (!string.IsNullOrWhiteSpace(IndexPath) && Directory.Exists(IndexPath))
        {
            dialog.InitialDirectory = IndexPath;
        }

        if (dialog.ShowDialog() == true)
        {
            IndexPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseTessDataPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecione a pasta tessdata"
        };

        if (!string.IsNullOrWhiteSpace(TessDataPath) && Directory.Exists(TessDataPath))
        {
            dialog.InitialDirectory = TessDataPath;
        }

        if (dialog.ShowDialog() == true)
        {
            TessDataPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void BrowseEmbeddingModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o modelo de embeddings (.onnx)",
            Filter = "Modelos ONNX (*.onnx)|*.onnx|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            EmbeddingModelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseEmbeddingTokenizer()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o vocabulário do tokenizer (vocab.txt)",
            Filter = "Vocabulário (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            EmbeddingTokenizerPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseWhisperModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o modelo Whisper (ggml .bin)",
            Filter = "Modelos Whisper (*.bin)|*.bin|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            WhisperModelPath = dialog.FileName;
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

            var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(_options));

            EmbeddingService? embeddingService = null;
            VectorStore? vectorStore = null;
            if (EmbeddingsEnabled)
            {
                embeddingService = new EmbeddingService(EmbeddingModelPath, EmbeddingTokenizerPath);
                vectorStore = new VectorStore(IndexPath);
            }

            using (embeddingService)
            using (vectorStore)
            {
                var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
                var progress = new Progress<string>(message => StatusMessage = message);
                var stats = await indexer.IndexFolderAsync(DocumentsFolder, IndexPath, _options.ChunkSize, _options.ChunkOverlap, progress);

                StatusMessage = $"Concluído: {stats.FilesProcessed} arquivos processados, " +
                                $"{stats.FilesSkipped} ignorados, {stats.ChunksIndexed} trechos indexados." +
                                (stats.Errors.Count > 0 ? $" ({stats.Errors.Count} erro(s))" : string.Empty);
            }

            // The vectorStore opened above is a different instance than the one inside
            // _engine — without this, AskAsync would keep serving the pre-reindex snapshot
            // until the engine is rebuilt for an unrelated config change.
            _engine?.InvalidateVectorCache();
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
        catch (OllamaNotAvailableException ex)
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
        var key = RagEngineKey.From(_options, TopK);
        if (_engine is null || _engineKey != key)
        {
            _engine?.Dispose();
            _engine = RagQueryEngineFactory.Create(_options, TopK);
            _engineKey = key;
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
        _options.LlmProvider = LlmProvider;
        _options.OllamaBaseUrl = OllamaBaseUrl;
        _options.OllamaModel = OllamaModel;
        _options.OcrEnabled = OcrEnabled;
        _options.TessDataPath = TessDataPath;
        _options.OcrLanguages = OcrLanguages;
        _options.EmbeddingsEnabled = EmbeddingsEnabled;
        _options.EmbeddingModelPath = EmbeddingModelPath;
        _options.EmbeddingTokenizerPath = EmbeddingTokenizerPath;
        _options.HybridBm25Weight = HybridBm25Weight;
        _options.SttEnabled = SttEnabled;
        _options.WhisperModelPath = WhisperModelPath;
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
