using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;

namespace QuestResume.Api.Services;

/// <summary>
/// Starts an <see cref="IndexScheduler"/> that periodically runs a full
/// <see cref="DocumentIndexer.IndexFolderAsync"/> when <see cref="AppOptions.ScheduledIndexingEnabled"/>
/// is set at startup (item 15 of Lote 4). Complements <see cref="AutoReindexHostedService"/>
/// (which reacts to filesystem changes): this one runs on a fixed wall-clock interval regardless
/// of whether any change was detected, which also acts as a safety net on filesystems where
/// <see cref="System.IO.FileSystemWatcher"/> is unreliable (e.g. some network shares).
/// Configuration is re-read from disk on every startup, same as <see cref="AutoReindexHostedService"/>.
/// </summary>
public sealed class ScheduledIndexingHostedService : IHostedService, IDisposable
{
    private readonly ConfigService _configService;
    private readonly RagEngineProvider _engineProvider;
    private readonly ILogger<ScheduledIndexingHostedService> _logger;
    private IndexScheduler? _scheduler;

    public ScheduledIndexingHostedService(ConfigService configService, RagEngineProvider engineProvider, ILogger<ScheduledIndexingHostedService> logger)
    {
        _configService = configService;
        _engineProvider = engineProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _configService.Load();

        if (!options.ScheduledIndexingEnabled || string.IsNullOrWhiteSpace(options.DocumentsFolder))
        {
            return Task.CompletedTask;
        }

        _scheduler = new IndexScheduler(
            TimeSpan.FromMinutes(Math.Max(1, options.ScheduledIndexingIntervalMinutes)),
            RunScheduledIndexingAsync,
            log: message => _logger.LogWarning("{Message}", message));

        _scheduler.Start();
        return Task.CompletedTask;
    }

    private async Task RunScheduledIndexingAsync(CancellationToken cancellationToken)
    {
        var options = _configService.Load();
        if (string.IsNullOrWhiteSpace(options.DocumentsFolder))
        {
            return;
        }

        var registry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options));

        EmbeddingService? embeddingService = null;
        VectorStore? vectorStore = null;
        if (options.EmbeddingsEnabled)
        {
            embeddingService = new EmbeddingService(options.EmbeddingModelPath, options.EmbeddingTokenizerPath);
            vectorStore = new VectorStore(options.IndexPath);
        }

        using (embeddingService)
        using (vectorStore)
        {
            var indexer = new DocumentIndexer(registry, embeddingService, vectorStore);
            var stats = await indexer.IndexFolderAsync(
                options.DocumentsFolder,
                options.IndexPath,
                options.ChunkSize,
                options.ChunkOverlap,
                cancellationToken: cancellationToken,
                maxFileSizeBytes: options.MaxFileSizeBytes,
                excludedFolders: options.ExcludedFolders,
                piiRedactionEnabled: options.PiiRedactionEnabled,
                parallelism: options.IndexingParallelism,
                incrementalIndexingEnabled: options.IncrementalIndexingEnabled,
                additionalFolders: options.AdditionalWatchedFolders,
                throttleDelayMs: options.IndexingThrottleDelayMs,
                prioritizeRecentFiles: options.PrioritizeRecentFiles).ConfigureAwait(false);

            _logger.LogInformation(
                "Indexação agendada concluída: {FilesProcessed} arquivo(s), {ChunksIndexed} trecho(s), {Errors} erro(s)",
                stats.FilesProcessed, stats.ChunksIndexed, stats.Errors.Count);
        }

        _engineProvider.InvalidateVectorCache();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _scheduler?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _scheduler?.Dispose();
    }
}
