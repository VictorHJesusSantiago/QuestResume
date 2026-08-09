namespace QuestResume.Core.Indexing;

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

        public void Start()
    {
        if (_loopTask is not null) return;

        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

        public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _loopTask?.Wait(TimeSpan.FromSeconds(30));
        }
        catch (AggregateException)
        {
            
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
            
        }
    }

    public void Dispose() => Stop();
}
