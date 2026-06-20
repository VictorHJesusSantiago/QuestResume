using QuestResume.Core.Configuration;
using QuestResume.Core.Persistence;

namespace QuestResume.Api.Services;

/// <summary>
/// Periodically rotates <c>audit.jsonl</c> so it never exceeds
/// <see cref="AppOptions.MaxAuditLogLines"/>. Running this in a BackgroundService
/// avoids an O(n) file rewrite on every <c>/api/ask</c> call.
/// </summary>
public sealed class AuditLogRotationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ConfigService _configService;
    private readonly ILogger<AuditLogRotationService> _logger;

    public AuditLogRotationService(ConfigService configService, ILogger<AuditLogRotationService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            TryRotate();
        }
    }

    private void TryRotate()
    {
        try
        {
            var options = _configService.Load();
            if (options.MaxAuditLogLines <= 0) return;

            new AuditLogRepository(options.IndexPath).Rotate(options.MaxAuditLogLines);
            _logger.LogDebug("Audit log rotacionado (max {Max} linhas)", options.MaxAuditLogLines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao rodar rotação do audit log");
        }
    }
}
