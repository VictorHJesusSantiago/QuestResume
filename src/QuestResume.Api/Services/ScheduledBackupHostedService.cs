using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;

namespace QuestResume.Api.Services;

/// <summary>
/// Inicia um <see cref="BackupScheduler"/> que periodicamente cria backups do índice com rotação/
/// retenção quando <see cref="AppOptions.ScheduledBackupEnabled"/> está ativo no startup (item 14
/// do Lote 6). Os backups vão para <c>&lt;IndexPath&gt;/../backups</c>. Mesmo padrão de
/// <see cref="ScheduledIndexingHostedService"/>.
/// </summary>
public sealed class ScheduledBackupHostedService : IHostedService, IDisposable
{
    private readonly ConfigService _configService;
    private readonly ILogger<ScheduledBackupHostedService> _logger;
    private BackupScheduler? _scheduler;

    public ScheduledBackupHostedService(ConfigService configService, ILogger<ScheduledBackupHostedService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _configService.Load();
        if (!options.ScheduledBackupEnabled || string.IsNullOrWhiteSpace(options.IndexPath))
        {
            return Task.CompletedTask;
        }

        var backupDir = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(options.IndexPath)) ?? options.IndexPath,
            "backups");

        _scheduler = new BackupScheduler(
            TimeSpan.FromHours(Math.Max(1, options.ScheduledBackupIntervalHours)),
            options.IndexPath,
            backupDir,
            options.BackupRetentionCount,
            log: message => _logger.LogInformation("{Message}", message));

        _scheduler.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _scheduler?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _scheduler?.Dispose();
}
