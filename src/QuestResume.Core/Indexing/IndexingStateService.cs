namespace QuestResume.Core.Indexing;

/// <summary>
/// Tracks whether an indexing run is currently in progress and exposes pause/resume/cancel
/// controls for it (item 16 of Lote 4). A single shared instance is meant to be registered per
/// front-end (API/Desktop) and passed into <see cref="DocumentIndexer.IndexFolderAsync"/> as its
/// <c>pauseHandle</c>/<c>cancellationToken</c> for the currently running job, so a future UI can
/// expose Pause/Resume/Cancel buttons without each front-end reinventing this bookkeeping.
///
/// Pausing does not stop already-in-flight file processing — it blocks each parallel worker
/// before it starts its *next* file (see the <c>pauseHandle?.Wait(ct)</c> check in
/// <see cref="DocumentIndexer.IndexFolderAsync"/>). Cancelling relies on the standard
/// <see cref="CancellationToken"/> plumbing already threaded through
/// <see cref="DocumentIndexer.IndexFolderAsync"/> and <see cref="DocumentIndexer.ReindexSingleFileAsync"/>.
/// </summary>
public sealed class IndexingStateService
{
    private readonly ManualResetEventSlim _pauseHandle = new(initialState: true);
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    /// <summary>True while an indexing run started via <see cref="BeginRun"/> hasn't yet called <see cref="EndRun"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>True when a running indexing job is currently paused (workers waiting to resume).</summary>
    public bool IsPaused => !_pauseHandle.IsSet;

    /// <summary>
    /// The <see cref="ManualResetEventSlim"/> to pass as <c>DocumentIndexer.IndexFolderAsync</c>'s
    /// <c>pauseHandle</c> parameter for the run just started via <see cref="BeginRun"/>.
    /// </summary>
    public ManualResetEventSlim PauseHandle => _pauseHandle;

    /// <summary>
    /// Marks a new indexing run as started and returns a linked <see cref="CancellationToken"/>
    /// that <see cref="Cancel"/> will trigger; combine it (via
    /// <see cref="CancellationTokenSource.CreateLinkedTokenSource"/>) with any caller-provided
    /// token before passing it to <c>IndexFolderAsync</c>.
    /// </summary>
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

    /// <summary>Marks the current run as finished (call in a <c>finally</c> around the indexing call).</summary>
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

    /// <summary>Pauses the current run (no-op if none is running or already paused).</summary>
    public void Pause() => _pauseHandle.Reset();

    /// <summary>Resumes a paused run (no-op if not paused).</summary>
    public void Resume() => _pauseHandle.Set();

    /// <summary>Cancels the current run (no-op if none is running). Also resumes if paused, so cancellation is observed promptly.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _pauseHandle.Set();
            _cts?.Cancel();
        }
    }
}
