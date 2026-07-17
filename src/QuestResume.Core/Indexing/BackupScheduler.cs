using QuestResume.Core.Persistence;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Dispara backups do índice (<see cref="IndexBackupService"/>) num intervalo fixo, com rotação/
/// retenção (mantém só os N backups mais recentes na pasta de destino, apaga os mais antigos).
/// Mesmo padrão de <see cref="IndexScheduler"/> (usa <see cref="PeriodicTimer"/>); é embrulhado num
/// <c>IHostedService</c> pela API, já que Core não depende de Microsoft.Extensions.Hosting.
/// Configurado por <see cref="Configuration.AppOptions.ScheduledBackupEnabled"/>,
/// <c>ScheduledBackupIntervalHours</c> e <c>BackupRetentionCount</c>.
/// </summary>
public sealed class BackupScheduler : IDisposable
{
    /// <summary>Prefixo dos arquivos de backup gerados automaticamente (usado também na rotação).</summary>
    public const string BackupPrefix = "questresume-backup-";

    private readonly TimeSpan _interval;
    private readonly string _indexPath;
    private readonly string _backupDir;
    private readonly int _retentionCount;
    private readonly IndexBackupService _backupService;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public BackupScheduler(
        TimeSpan interval,
        string indexPath,
        string backupDir,
        int retentionCount,
        IndexBackupService? backupService = null,
        Action<string>? log = null)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "O intervalo de backup deve ser maior que zero.");

        _interval = interval;
        _indexPath = indexPath;
        _backupDir = backupDir;
        _retentionCount = Math.Max(1, retentionCount);
        _backupService = backupService ?? new IndexBackupService();
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
        try { _loopTask?.Wait(TimeSpan.FromSeconds(30)); }
        catch (AggregateException) { }
        finally { _loopTask = null; _cts?.Dispose(); _cts = null; }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try { await RunBackupOnceAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { _log?.Invoke($"Backup agendado falhou: {ex.Message}"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Cria um backup timestamped e aplica a retenção. Exposto para teste/execução manual.</summary>
    public async Task<string> RunBackupOnceAsync(CancellationToken cancellationToken = default)
    {
        IODirectory.CreateDirectory(_backupDir);
        var fileName = $"{BackupPrefix}{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
        var backupPath = Path.Combine(_backupDir, fileName);
        await _backupService.CreateBackupAsync(_indexPath, backupPath, cancellationToken).ConfigureAwait(false);
        _log?.Invoke($"Backup criado: {backupPath}");
        ApplyRetention();
        return backupPath;
    }

    /// <summary>Mantém apenas os <see cref="_retentionCount"/> backups mais recentes.</summary>
    public void ApplyRetention()
    {
        if (!IODirectory.Exists(_backupDir)) return;
        var backups = IODirectory.EnumerateFiles(_backupDir, $"{BackupPrefix}*.zip")
            .OrderByDescending(f => f, StringComparer.Ordinal)
            .ToList();
        foreach (var old in backups.Skip(_retentionCount))
        {
            try { File.Delete(old); _log?.Invoke($"Backup antigo removido: {old}"); }
            catch (Exception ex) { _log?.Invoke($"Falha ao remover backup antigo '{old}': {ex.Message}"); }
        }
    }

    public void Dispose() => Stop();
}
