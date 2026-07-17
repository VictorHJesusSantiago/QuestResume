namespace QuestResume.Core.Indexing;

/// <summary>
/// Runs a given async callback (typically <see cref="DocumentIndexer.IndexFolderAsync"/> wired
/// up by the caller) on a fixed interval using <see cref="PeriodicTimer"/> — the simplest
/// built-in .NET mechanism for a periodic background job, chosen over a raw
/// <see cref="System.Threading.Timer"/> because it awaits cleanly (no re-entrant tick callbacks
/// to guard against) and over a cron-style library because a single fixed interval is all
/// <see cref="Configuration.AppOptions.ScheduledIndexingEnabled"/> needs.
///
/// Wired into a host via a small <c>IHostedService</c> in each front-end (see
/// <c>ScheduledIndexingHostedService</c> in QuestResume.Api) rather than living here directly,
/// since Core has no dependency on Microsoft.Extensions.Hosting.
/// </summary>
public sealed class IndexScheduler : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _runAsync;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public IndexScheduler(TimeSpan interval, Func<CancellationToken, Task> runAsync, Action<string>? log = null)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo de agendamento deve ser maior que zero.");
        }

        _interval = interval;
        _runAsync = runAsync;
        _log = log;
    }

    /// <summary>Starts the periodic loop. A no-op if already started.</summary>
    public void Start()
    {
        if (_loopTask is not null) return;

        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

    /// <summary>Stops the periodic loop and waits for any in-flight run to observe cancellation.</summary>
    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            // Expected when the loop task ends via OperationCanceledException.
        }
        finally
        {
            _loopTask = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    await _runAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Falha na indexação agendada: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop()/Dispose().
        }
    }

    public void Dispose() => Stop();
}
