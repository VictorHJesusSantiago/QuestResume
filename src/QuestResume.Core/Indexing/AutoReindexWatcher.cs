namespace QuestResume.Core.Indexing;

/// <summary>
/// Watches a folder for changes (create/change/delete/rename, including subdirectories) using
/// <see cref="FileSystemWatcher"/> and invokes a reindex callback after a debounce window, so a
/// batch copy/edit of many files triggers a single reindex instead of one per file event.
/// </summary>
public sealed class AutoReindexWatcher : IDisposable
{
    private readonly string _folderPath;
    private readonly Func<CancellationToken, Task> _onReindexRequested;
    private readonly TimeSpan _debounce;
    private readonly Action<string>? _log;

    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private bool _reindexInProgress;
    private bool _pendingWhileRunning;
    private bool _disposed;

    /// <param name="folderPath">Folder to watch recursively.</param>
    /// <param name="onReindexRequested">Callback invoked (once) after the debounce window elapses.</param>
    /// <param name="debounce">Quiet period after the last change before reindexing runs. Default 5s.</param>
    /// <param name="log">Optional PT-BR status logger.</param>
    public AutoReindexWatcher(
        string folderPath,
        Func<CancellationToken, Task> onReindexRequested,
        TimeSpan? debounce = null,
        Action<string>? log = null)
    {
        _folderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
        _onReindexRequested = onReindexRequested ?? throw new ArgumentNullException(nameof(onReindexRequested));
        _debounce = debounce ?? TimeSpan.FromSeconds(5);
        _log = log;
    }

    /// <summary>Starts watching. Safe to call once; call <see cref="Stop"/>/<see cref="Dispose"/> before starting again.</summary>
    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        if (!Directory.Exists(_folderPath))
        {
            _log?.Invoke($"Auto-reindexação não iniciada: pasta não encontrada: {_folderPath}");
            return;
        }

        _watcher = new FileSystemWatcher(_folderPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
        };

        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnFileSystemEvent;
        _watcher.Error += OnWatcherError;

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        _watcher.EnableRaisingEvents = true;

        _log?.Invoke($"Auto-reindexação ativada, monitorando: {_folderPath}");
    }

    /// <summary>Stops watching and cancels any pending (not-yet-fired) debounce timer.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnFileSystemEvent;
                _watcher.Created -= OnFileSystemEvent;
                _watcher.Deleted -= OnFileSystemEvent;
                _watcher.Renamed -= OnFileSystemEvent;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;
            }

            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _log?.Invoke($"Erro no monitoramento de arquivos: {e.GetException().Message}");
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            // Restart (not just start) the timer on every event, so the debounce window is
            // measured from the *last* change — a batch copy of many files only reindexes once,
            // after the copy has been quiet for the debounce period.
            _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnDebounceElapsed(object? state)
    {
        lock (_lock)
        {
            if (_reindexInProgress)
            {
                // A reindex is already running; remember to run one more time once it finishes,
                // so changes made during the run aren't silently dropped.
                _pendingWhileRunning = true;
                return;
            }

            _reindexInProgress = true;
        }

        try
        {
            _log?.Invoke("Alterações detectadas, reindexando automaticamente...");
            await _onReindexRequested(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Falha na reindexação automática: {ex.Message}");
        }
        finally
        {
            bool runAgain;
            lock (_lock)
            {
                _reindexInProgress = false;
                runAgain = _pendingWhileRunning;
                _pendingWhileRunning = false;
            }

            if (runAgain)
            {
                _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
