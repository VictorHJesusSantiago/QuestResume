using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuestResume.Desktop;
using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Rag;
using QuestResume.Core.Services;

namespace QuestResume.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ConfigService _configService;
    private readonly AppOptions _options;

    // Shared DirectoryReader kept open across all SearchService calls in this process.
    // Refreshed via OpenIfChanged on each acquisition — eliminates repeated FSDirectory
    // open/close cycles that degrade after each index operation.
    private readonly LuceneIndexManager _indexManager = new();

    private RagQueryEngine? _engine;
    private RagEngineKey? _engineKey;
    private AutoReindexWatcher? _autoReindexWatcher;

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
    private bool piiRedactionEnabled;

    [ObservableProperty]
    private int gpuLayerCount;

    [ObservableProperty]
    private int indexingParallelism = Math.Max(1, Environment.ProcessorCount);

    [ObservableProperty]
    private bool incrementalIndexingEnabled;

    [ObservableProperty]
    private bool autoReindexEnabled;

    [ObservableProperty]
    private bool llmFallbackEnabled;

    [ObservableProperty]
    private string questionText = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string studyDocumentPath = string.Empty;

    [ObservableProperty]
    private int studyCount = 5;

    [ObservableProperty]
    private string translateTargetLanguage = "en";

    [ObservableProperty]
    private string selectedCollection = QuestResume.Core.Persistence.CollectionStore.DefaultName;

    public ObservableCollection<string> AvailableCollections { get; } = new();

    /// <summary>
    /// Caminho físico efetivo do índice para a coleção selecionada (ver
    /// <see cref="QuestResume.Core.Persistence.CollectionStore"/>). Usado em todas as operações
    /// de índice/busca/pergunta em vez de <see cref="IndexPath"/> diretamente, que permanece
    /// como o caminho base configurável em Configurações.
    /// </summary>
    private string EffectiveIndexPath =>
        new QuestResume.Core.Persistence.CollectionStore(IndexPath).ResolvePath(SelectedCollection);

    partial void OnSelectedCollectionChanged(string value)
    {
        _engine?.InvalidateVectorCache();
        _engine?.Dispose();
        _engine = null;
        _engineKey = null;
        LoadDocuments();
        RefreshStatus();
    }

    [RelayCommand]
    private void LoadCollections()
    {
        try
        {
            var store = new QuestResume.Core.Persistence.CollectionStore(IndexPath);
            AvailableCollections.Clear();
            foreach (var collection in store.List())
            {
                AvailableCollections.Add(collection.Nome);
            }
        }
        catch
        {
            // Falha ao carregar coleções não deve impedir o uso da coleção "default".
        }
    }

    [RelayCommand]
    private void CreateCollection(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var store = new QuestResume.Core.Persistence.CollectionStore(IndexPath);
            store.Create(name);
            LoadCollections();
            SelectedCollection = name;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao criar coleção: {ex.Message}";
        }
    }

    public ObservableCollection<ChatEntry> Messages { get; } = new();

    public ObservableCollection<IndexedDocumentViewModel> IndexedDocuments { get; } = new();

    public ObservableCollection<string> IndexErrors { get; } = new();

    public ObservableCollection<DuplicateFile> IndexDuplicates { get; } = new();

    public ObservableCollection<FlashcardViewModel> Flashcards { get; } = new();

    public ObservableCollection<QuizQuestionViewModel> QuizQuestions { get; } = new();

    /// <summary>Plugins de extração de terceiros carregados de %LOCALAPPDATA%\QuestResume\plugins, exibidos em Configurações.</summary>
    public ObservableCollection<string> LoadedPlugins { get; } = new();

    public string[] LlmProviderOptions { get; } = { "LlamaSharp", "Ollama" };

    public string[] TranslateLanguageOptions { get; } = { "en", "es", "fr", "de", "it", "ja", "zh", "pt" };

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
        piiRedactionEnabled = _options.PiiRedactionEnabled;
        gpuLayerCount = _options.GpuLayerCount;
        indexingParallelism = _options.IndexingParallelism;
        incrementalIndexingEnabled = _options.IncrementalIndexingEnabled;
        autoReindexEnabled = _options.AutoReindexEnabled;
        llmFallbackEnabled = _options.LlmFallbackEnabled;

        LoadCollections();
        RefreshStatus();
        LoadDocuments();
        UpdateAutoReindexWatcher();
        LoadPlugins();
    }

    private void LoadPlugins()
    {
        LoadedPlugins.Clear();
        var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(_options), loadPlugins: true);
        foreach (var plugin in registry.LoadedPlugins)
        {
            LoadedPlugins.Add($"{plugin.ExtractorTypeName} ({plugin.AssemblyFileName}): {string.Join(", ", plugin.SupportedExtensions)}");
        }

        if (LoadedPlugins.Count == 0)
        {
            LoadedPlugins.Add("Nenhum plugin carregado.");
        }
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

            var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(_options), loadPlugins: true);

            EmbeddingService? embeddingService = null;
            VectorStore? vectorStore = null;
            if (EmbeddingsEnabled)
            {
                embeddingService = new EmbeddingService(EmbeddingModelPath, EmbeddingTokenizerPath);
                vectorStore = new VectorStore(EffectiveIndexPath);
            }

            using (embeddingService)
            using (vectorStore)
            {
                var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
                var progress = new Progress<string>(message => StatusMessage = message);
                var stats = await indexer.IndexFolderAsync(DocumentsFolder, EffectiveIndexPath, _options.ChunkSize, _options.ChunkOverlap, progress,
                    maxFileSizeBytes: _options.MaxFileSizeBytes, excludedFolders: _options.ExcludedFolders,
                    piiRedactionEnabled: _options.PiiRedactionEnabled, parallelism: _options.IndexingParallelism,
                    incrementalIndexingEnabled: _options.IncrementalIndexingEnabled);

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

        var search = new SearchService(EffectiveIndexPath, _indexManager);
        foreach (var file in search.GetIndexedFiles())
        {
            IndexedDocuments.Add(new IndexedDocumentViewModel
            {
                SourcePath = file.SourcePath,
                FileName = file.FileName,
                ChunkCount = file.ChunkCount,
                TagsInput = string.Join(", ", file.Tags),
                Summary = file.Summary ?? string.Empty
            });
        }

        var report = IndexReport.Load(EffectiveIndexPath);
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
            var search = new SearchService(EffectiveIndexPath, _indexManager);
            var removed = search.RemoveDocument(sourcePath);

            if (removed == 0)
            {
                StatusMessage = $"Documento não encontrado no índice: {sourcePath}";
                return;
            }

            if (EmbeddingsEnabled)
            {
                using var vectorStore = new VectorStore(EffectiveIndexPath);
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
            var search = new SearchService(EffectiveIndexPath, _indexManager);
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

    /// <summary>
    /// Moves keyboard focus to the question box (Ctrl+K shortcut, see <c>MainWindow.xaml</c>
    /// <c>Window.InputBindings</c>). The Desktop app has no global search box like the Web UI, so
    /// this targets the closest equivalent primary input field.
    /// </summary>
    [RelayCommand]
    private void FocusQuestion()
    {
        if (System.Windows.Application.Current?.MainWindow is MainWindow window)
        {
            window.QuestionTextBox.Focus();
        }
    }

    [RelayCommand]
    private async Task AskRelatedQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;
        QuestionText = question;
        await AskAsync();
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

            var search = new SearchService(EffectiveIndexPath, _indexManager);
            if (!search.IndexExists())
            {
                Messages.Add(new ChatEntry { Role = "Erro", Text = "Nenhum índice encontrado. Indexe uma pasta primeiro." });
                return;
            }

            var engine = GetEngine();
            var result = await engine.AskAsync(question, TopK, history);

            var sources = result.Sources
                .Select(s => new SourceReference { FileName = s.FileName, SourcePath = s.SourcePath, ChunkIndex = s.ChunkIndex })
                .DistinctBy(s => s.SourcePath)
                .ToList();

            Messages.Add(new ChatEntry
            {
                Role = "QuestResume",
                Text = result.Answer,
                Sources = sources.Count > 0 ? sources : null,
                RelatedQuestions = result.RelatedQuestions
            });
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
        UpdateAutoReindexWatcher();
    }

    /// <summary>
    /// Starts or stops the <see cref="AutoReindexWatcher"/> to match <see cref="AutoReindexEnabled"/>
    /// and <see cref="DocumentsFolder"/>, called after every config save so a toggle takes effect
    /// immediately without restarting the app.
    /// </summary>
    private void UpdateAutoReindexWatcher()
    {
        _autoReindexWatcher?.Dispose();
        _autoReindexWatcher = null;

        if (!AutoReindexEnabled || string.IsNullOrWhiteSpace(DocumentsFolder) || !Directory.Exists(DocumentsFolder))
        {
            return;
        }

        _autoReindexWatcher = new AutoReindexWatcher(
            DocumentsFolder,
            _ => IndexAsync(),
            log: message => StatusMessage = message);
        _autoReindexWatcher.Start();
    }

    /// <summary>Compacts the whole index folder into a .zip chosen via a save dialog.</summary>
    [RelayCommand]
    private async Task BackupIndexAsync()
    {
        if (string.IsNullOrWhiteSpace(IndexPath) || !Directory.Exists(IndexPath))
        {
            StatusMessage = "Nenhum índice para fazer backup.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Arquivo ZIP (*.zip)|*.zip",
            FileName = $"questresume-backup-{DateTime.Now:yyyy-MM-dd-HHmmss}.zip"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Fazendo backup do índice...";
        try
        {
            var backupService = new QuestResume.Core.Persistence.IndexBackupService();
            await backupService.CreateBackupAsync(IndexPath, dialog.FileName);
            StatusMessage = $"Backup concluído: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao fazer backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Restores a previously created backup .zip into the current index path.</summary>
    [RelayCommand]
    private async Task RestoreIndexAsync()
    {
        if (string.IsNullOrWhiteSpace(IndexPath))
        {
            StatusMessage = "Configure a pasta do índice antes de restaurar.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Arquivo ZIP (*.zip)|*.zip"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Restaurando backup...";
        try
        {
            var backupService = new QuestResume.Core.Persistence.IndexBackupService();
            await backupService.RestoreBackupAsync(dialog.FileName, IndexPath);
            _engine?.InvalidateVectorCache();
            LoadDocuments();
            RefreshStatus();
            StatusMessage = "Restauração concluída.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao restaurar backup: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens an indexed source file in its associated default application (e.g. PDF viewer,
    /// Word), so the user can jump from a citation in the chat directly to the original
    /// document. <see cref="ProcessStartInfo.UseShellExecute"/> delegates to the OS's file
    /// association instead of trying to execute the file directly.
    /// </summary>
    /// <summary>
    /// Exports the current chat history (<see cref="Messages"/>) as a Markdown file chosen via
    /// a save dialog, so the user can keep a record of a Q&amp;A session outside the app.
    /// </summary>
    [RelayCommand]
    private void ExportChat()
    {
        if (Messages.Count == 0)
        {
            StatusMessage = "Não há conversa para exportar.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            FileName = $"conversa-questresume-{DateTime.Now:yyyy-MM-dd-HHmmss}.md"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var lines = new List<string> { "# Conversa - QuestResume", string.Empty };
        foreach (var entry in Messages)
        {
            lines.Add($"## {entry.Role}");
            lines.Add(string.Empty);
            lines.Add(entry.Text);
            lines.Add(string.Empty);

            if (entry.Sources is { Count: > 0 } sources)
            {
                lines.Add("Fontes: " + string.Join(", ", sources.Select(s => s.FileName).Distinct()));
                lines.Add(string.Empty);
            }
        }

        try
        {
            File.WriteAllLines(dialog.FileName, lines);
            StatusMessage = $"Conversa exportada para: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao exportar conversa: {ex.Message}";
        }
    }

    /// <summary>
    /// Shows the exact chunk of text used to answer the question (mirroring the Web UI's
    /// clickable citations), via a simple message box since the Desktop app has no modal
    /// infrastructure yet.
    /// </summary>
    [RelayCommand]
    private void ViewSourceChunk(SourceReference source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            var search = new SearchService(EffectiveIndexPath, _indexManager);
            var chunks = search.GetChunksByPath(source.SourcePath);
            if (chunks.Count == 0)
            {
                System.Windows.MessageBox.Show("Documento não encontrado no índice.", source.FileName);
                return;
            }

            var chunk = chunks.FirstOrDefault(c => c.ChunkIndex == source.ChunkIndex) ?? chunks[0];
            System.Windows.MessageBox.Show(
                chunk.ChunkText,
                $"{source.FileName} — trecho {chunk.ChunkIndex + 1} de {chunks.Count}",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.None);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao carregar trecho: {ex.Message}";
        }
    }

    /// <summary>
    /// Exports the current chat history as a PDF using <see cref="ChatPdfExporter"/> (mirrors
    /// <see cref="ExportChat"/>, which exports to Markdown).
    /// </summary>
    [RelayCommand]
    private void ExportChatPdf()
    {
        if (Messages.Count == 0)
        {
            StatusMessage = "Não há conversa para exportar.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"conversa-questresume-{DateTime.Now:yyyy-MM-dd-HHmmss}.pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var turns = new List<ChatExportTurn>();
        for (var i = 0; i < Messages.Count - 1; i++)
        {
            if (Messages[i].Role == "Você" && Messages[i + 1].Role == "QuestResume")
            {
                var answerEntry = Messages[i + 1];
                turns.Add(new ChatExportTurn(
                    Messages[i].Text,
                    answerEntry.Text,
                    answerEntry.Sources?.Select(s => s.FileName).Distinct().ToList()));
            }
        }

        if (turns.Count == 0)
        {
            StatusMessage = "Não há conversa para exportar.";
            return;
        }

        try
        {
            var pdfBytes = ChatPdfExporter.Export("Conversa - QuestResume", turns);
            File.WriteAllBytes(dialog.FileName, pdfBytes);
            StatusMessage = $"Conversa exportada para: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao exportar PDF: {ex.Message}";
        }
    }

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

    /// <summary>
    /// Extrai dados tabulares do documento indicado via LLM e salva o resultado (JSON ou CSV,
    /// conforme escolha do usuário no diálogo de salvar) em um arquivo.
    /// </summary>
    [RelayCommand]
    private async Task ExtractTableAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Extraindo tabela (isso pode demorar na primeira chamada, enquanto o modelo carrega)...";
        try
        {
            SaveCurrentOptions();
            var engine = GetEngine();
            var llm = await engine.GetLlmProviderAsync();
            var service = new StructuredExtractionService(engine.SearchService.GetChunksByPath, llm);
            var result = await service.ExtractTableAsync(sourcePath, null);

            var dialog = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json|CSV (*.csv)|*.csv",
                FileName = $"tabela-{Path.GetFileNameWithoutExtension(sourcePath)}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                var content = dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? result.Csv : result.Json;
                await File.WriteAllTextAsync(dialog.FileName, content);
                StatusMessage = $"Tabela extraída e salva em: {dialog.FileName}";
            }
            else
            {
                StatusMessage = "Tabela extraída (não salva).";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao extrair tabela: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Gera flashcards de estudo (pergunta/resposta) para o documento em <see cref="StudyDocumentPath"/>.</summary>
    [RelayCommand]
    private async Task GenerateFlashcardsAsync()
    {
        if (string.IsNullOrWhiteSpace(StudyDocumentPath))
        {
            StatusMessage = "Selecione um documento indexado para gerar flashcards.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Gerando flashcards...";
        try
        {
            SaveCurrentOptions();
            var engine = GetEngine();
            var llm = await engine.GetLlmProviderAsync();
            var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
            var cards = await service.GenerateFlashcardsAsync(StudyDocumentPath, StudyCount);

            Flashcards.Clear();
            foreach (var card in cards)
            {
                Flashcards.Add(new FlashcardViewModel { Question = card.Question, Answer = card.Answer });
            }

            StatusMessage = $"{cards.Count} flashcard(s) gerado(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao gerar flashcards: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Gera um quiz de múltipla escolha para o documento em <see cref="StudyDocumentPath"/>.</summary>
    [RelayCommand]
    private async Task GenerateQuizAsync()
    {
        if (string.IsNullOrWhiteSpace(StudyDocumentPath))
        {
            StatusMessage = "Selecione um documento indexado para gerar o quiz.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Gerando quiz...";
        try
        {
            SaveCurrentOptions();
            var engine = GetEngine();
            var llm = await engine.GetLlmProviderAsync();
            var service = new FlashcardService(engine.SearchService.GetChunksByPath, llm);
            var questions = await service.GenerateQuizAsync(StudyDocumentPath, StudyCount);

            QuizQuestions.Clear();
            foreach (var question in questions)
            {
                QuizQuestions.Add(new QuizQuestionViewModel
                {
                    Question = question.Question,
                    Options = question.Options,
                    CorrectOptionIndex = question.CorrectOptionIndex
                });
            }

            StatusMessage = $"{questions.Count} pergunta(s) de quiz gerada(s).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao gerar quiz: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectQuizOption(QuizOptionSelection selection)
    {
        selection.Question.SelectOption(selection.OptionIndex);
    }

    /// <summary>
    /// Traduz o texto de uma resposta do chat para <see cref="TranslateTargetLanguage"/> e
    /// adiciona o resultado como uma nova entrada na conversa.
    /// </summary>
    [RelayCommand]
    private async Task TranslateAsync(ChatEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Text))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Traduzindo...";
        try
        {
            SaveCurrentOptions();
            var engine = GetEngine();
            var llm = await engine.GetLlmProviderAsync();
            var service = new TranslationService(llm);
            var translated = await service.TranslateAsync(entry.Text, TranslateTargetLanguage);

            Messages.Add(new ChatEntry { Role = $"Tradução ({TranslateTargetLanguage})", Text = translated });
            StatusMessage = "Tradução concluída.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erro ao traduzir: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RagQueryEngine GetEngine()
    {
        var effectiveOptions = _options.Clone();
        effectiveOptions.IndexPath = EffectiveIndexPath;

        var key = RagEngineKey.From(effectiveOptions, TopK);
        if (_engine is null || _engineKey != key)
        {
            _engine?.Dispose();
            _engine = RagQueryEngineFactory.Create(effectiveOptions, TopK);
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
        _options.PiiRedactionEnabled = PiiRedactionEnabled;
        _options.GpuLayerCount = GpuLayerCount;
        _options.IndexingParallelism = IndexingParallelism;
        _options.IncrementalIndexingEnabled = IncrementalIndexingEnabled;
        _options.AutoReindexEnabled = AutoReindexEnabled;
        _options.LlmFallbackEnabled = LlmFallbackEnabled;
        _configService.Save(_options);
    }

    private void RefreshStatus()
    {
        var search = new SearchService(EffectiveIndexPath, _indexManager);
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
        _indexManager.Dispose();
        _autoReindexWatcher?.Dispose();
    }
}
