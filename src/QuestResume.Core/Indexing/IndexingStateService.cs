namespace QuestResume.Core.Indexing;

public sealed class IndexingStateService
{
    private readonly ManualResetEventSlim _pauseHandle = new(initialState: true);
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

        public bool IsRunning { get; private set; }

        public bool IsPaused => !_pauseHandle.IsSet;

        public ManualResetEventSlim PauseHandle => _pauseHandle;

        public CancellationToken BeginRun()
    {
        lock (_lock)
        {
            _cts = new CancellationTokenSource();
            _pauseHandle.Set();
            IsRunning = true;
            return _cts.Token;
        }
    }

        public void EndRun()
    {
        lock (_lock)
        {
            IsRunning = false;
            _pauseHandle.Set();
            _cts?.Dispose();
            _cts = null;
        }
    }

        public void Pause() => _pauseHandle.Reset();

        public void Resume() => _pauseHandle.Set();

        public void Cancel()
    {
        lock (_lock)
        {
            _pauseHandle.Set();
            _cts?.Cancel();
        }
    }
}
