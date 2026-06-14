using System.Collections.ObjectModel;
using System.Diagnostics;
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
using QuestResume.Core.Models;
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
    private bool rerankingEnabled;

    [ObservableProperty]
    private string rerankingModelPath = string.Empty;

    [ObservableProperty]
    private string rerankingTokenizerPath = string.Empty;

    [ObservableProperty]
    private string questionText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public ObservableCollection<ChatEntry> Messages { get; } = new();

    public ObservableCollection<IndexedDocumentViewModel> IndexedDocuments { get; } = new();

    public ObservableCollection<string> IndexErrors { get; } = new();

    public ObservableCollection<DuplicateFile> IndexDuplicates { get; } = new();

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
        rerankingEnabled = _options.RerankingEnabled;
        rerankingModelPath = _options.RerankingModelPath;
        rerankingTokenizerPath = _options.RerankingTokenizerPath;

        RefreshStatus();
        LoadDocuments();
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
    private void BrowseRerankingModel()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o modelo de re-ranking (cross-encoder, .onnx)",
            Filter = "Modelos ONNX (*.onnx)|*.onnx|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RerankingModelPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseRerankingTokenizer()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o vocabulário do tokenizer de re-ranking (vocab.txt)",
            Filter = "Vocabulário (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RerankingTokenizerPath = dialog.FileName;
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
            LoadDocuments();
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

    /// <summary>
    /// Refreshes the "Documentos" tab with the files currently in the index plus the errors
    /// and duplicates detected during the last <see cref="IndexAsync"/> run.
    /// </summary>
    [RelayCommand]
    private void LoadDocuments()
    {
        IndexedDocuments.Clear();
        IndexErrors.Clear();
        IndexDuplicates.Clear();

        var search = new SearchService(IndexPath);
        foreach (var file in search.GetIndexedFiles())
        {
            IndexedDocuments.Add(new IndexedDocumentViewModel
            {
                SourcePath = file.SourcePath,
                FileName = file.FileName,
                ChunkCount = file.ChunkCount,
                TagsInput = string.Join(", ", file.Tags)
            });
        }

        var report = IndexReport.Load(IndexPath);
        foreach (var error in report.Errors)
        {
            IndexErrors.Add(error);
        }

        foreach (var duplicate in report.Duplicates)
        {
            IndexDuplicates.Add(duplicate);
        }
    }

    /// <summary>
    /// Removes a single document from the Lucene index (and the vector store, if embeddings are
    /// enabled) without rebuilding the whole index from the documents folder.
    /// </summary>
    [RelayCommand]
    private void RemoveDocument(string sourcePath)
    {
        try
        {
            var search = new SearchService(IndexPath);
            var removed = search.RemoveDocument(sourcePath);

            if (removed == 0)
            {
                StatusMessage = $"Documento não encontrado no índice: {sourcePath}";
                return;
            }

            if (EmbeddingsEnabled)
            {
                using var vectorStore = new VectorStore(IndexPath);
                vectorStore.RemoveBySourcePath(sourcePath);
            }

            _engine?.InvalidateVectorCache();
            LoadDocuments();
            StatusMessage = $"Documento removido do índice ({removed} trecho(s)): {sourcePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao remover documento: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves the comma-separated tags the user typed into <see cref="IndexedDocumentViewModel.TagsInput"/>
    /// for the given document, normalizing the displayed value to match what was actually stored.
    /// </summary>
    [RelayCommand]
    private void SaveTags(IndexedDocumentViewModel document)
    {
        try
        {
            var search = new SearchService(IndexPath);
            var tags = document.TagsInput.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            search.SetTags(document.SourcePath, tags);
            document.TagsInput = string.Join(", ", search.GetTags(document.SourcePath));
            StatusMessage = $"Tags atualizadas: {document.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao salvar tags: {ex.Message}";
        }
    }

    /// <summary>
    /// Pairs up consecutive "Você"/"QuestResume" entries from <see cref="Messages"/> into
    /// <see cref="ChatTurn"/>s so <see cref="RagQueryEngine.AskAsync"/> can use them as
    /// short-term conversational memory for the next question.
    /// </summary>
    private IReadOnlyList<ChatTurn> BuildHistory()
    {
        var history = new List<ChatTurn>();
        for (var i = 0; i < Messages.Count - 1; i++)
        {
            if (Messages[i].Role == "Você" && Messages[i + 1].Role == "QuestResume")
            {
                history.Add(new ChatTurn(Messages[i].Text, Messages[i + 1].Text));
            }
        }

        return history;
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        var question = QuestionText.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        var history = BuildHistory();

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
            var result = await engine.AskAsync(question, TopK, history);

            var sources = result.Sources
                .Select(s => new SourceReference { FileName = s.FileName, SourcePath = s.SourcePath })
                .DistinctBy(s => s.SourcePath)
                .ToList();

            Messages.Add(new ChatEntry { Role = "QuestResume", Text = result.Answer, Sources = sources.Count > 0 ? sources : null });
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

    /// <summary>
    /// Opens an indexed source file in its associated default application (e.g. PDF viewer,
    /// Word), so the user can jump from a citation in the chat directly to the original
    /// document. <see cref="ProcessStartInfo.UseShellExecute"/> delegates to the OS's file
    /// association instead of trying to execute the file directly.
    /// </summary>
    [RelayCommand]
    private void OpenSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusMessage = "Arquivo não encontrado: " + path;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
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
        _options.RerankingEnabled = RerankingEnabled;
        _options.RerankingModelPath = RerankingModelPath;
        _options.RerankingTokenizerPath = RerankingTokenizerPath;
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
