namespace QuestResume.Core.Indexing;

public sealed class AutoReindexWatcher : IDisposable
{
    private readonly IReadOnlyList<string> _folderPaths;
    private readonly Func<CancellationToken, Task> _onReindexRequested;
    private readonly TimeSpan _debounce;
    private readonly Action<string>? _log;

    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _debounceTimer;
    private readonly object _lock = new();
    private bool _reindexInProgress;
    private bool _pendingWhileRunning;
    private bool _disposed;

        public AutoReindexWatcher(
        string folderPath,
        Func<CancellationToken, Task> onReindexRequested,
        TimeSpan? debounce = null,
        Action<string>? log = null,
        IEnumerable<string>? additionalFolders = null)
    {
        if (folderPath is null) throw new ArgumentNullException(nameof(folderPath));

        var folders = new List<string> { folderPath };
        if (additionalFolders is not null)
        {
            foreach (var extra in additionalFolders)
            {
                if (!string.IsNullOrWhiteSpace(extra) && !folders.Contains(extra, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(extra);
                }
            }
        }

        _folderPaths = folders;
        _onReindexRequested = onReindexRequested ?? throw new ArgumentNullException(nameof(onReindexRequested));
        _debounce = debounce ?? TimeSpan.FromSeconds(5);
        _log = log;
    }

        public void Start()
    {
        if (_watchers.Count > 0)
        {
            return;
        }

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

        foreach (var folder in _folderPaths)
        {
            if (!Directory.Exists(folder))
            {
                _log?.Invoke($"Auto-reindexação não iniciada para a pasta (não encontrada): {folder}");
                continue;
            }

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
            };

            watcher.Changed += OnFileSystemEvent;
            watcher.Created += OnFileSystemEvent;
            watcher.Deleted += OnFileSystemEvent;
            watcher.Renamed += OnFileSystemEvent;
            watcher.Error += OnWatcherError;

            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);

            _log?.Invoke($"Auto-reindexação ativada, monitorando: {folder}");
        }
    }

        public void Stop()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnFileSystemEvent;
                watcher.Created -= OnFileSystemEvent;
                watcher.Deleted -= OnFileSystemEvent;
                watcher.Renamed -= OnFileSystemEvent;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }
            _watchers.Clear();

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
            
            
            
            _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private async void OnDebounceElapsed(object? state)
    {
        lock (_lock)
        {
            if (_reindexInProgress)
            {
                
                
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
