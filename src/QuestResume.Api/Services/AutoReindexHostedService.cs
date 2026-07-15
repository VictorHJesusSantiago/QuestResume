using QuestResume.Core.Configuration;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Indexing;

namespace QuestResume.Api.Services;

/// <summary>
/// Starts an <see cref="AutoReindexWatcher"/> on <see cref="AppOptions.DocumentsFolder"/> when
/// <see cref="AppOptions.AutoReindexEnabled"/> is set at startup, triggering a full
/// <see cref="DocumentIndexer.IndexFolderAsync"/> run whenever the folder changes (debounced).
/// Configuration is re-read from disk on every startup; changing the toggle via
/// <c>PUT /api/config</c> takes effect the next time the API process restarts.
/// </summary>
public sealed class AutoReindexHostedService : IHostedService, IDisposable
{
    private readonly ConfigService _configService;
    private readonly RagEngineProvider _engineProvider;
    private readonly ILogger<AutoReindexHostedService> _logger;
    private AutoReindexWatcher? _watcher;

    public AutoReindexHostedService(ConfigService configService, RagEngineProvider engineProvider, ILogger<AutoReindexHostedService> logger)
    {
        _configService = configService;
        _engineProvider = engineProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _configService.Load();

        if (!options.AutoReindexEnabled || string.IsNullOrWhiteSpace(options.DocumentsFolder))
        {
            return Task.CompletedTask;
        }

        _watcher = new AutoReindexWatcher(
            options.DocumentsFolder,
            ReindexAsync,
            log: message => _logger.LogInformation("{Message}", message),
            additionalFolders: options.AdditionalWatchedFolders);

        _watcher.Start();
        return Task.CompletedTask;
    }

    private async Task ReindexAsync(CancellationToken cancellationToken)
    {
        var options = _configService.Load();

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
                additionalFolders: options.AdditionalWatchedFolders).ConfigureAwait(false);

            _logger.LogInformation(
                "Reindexação automática concluída: {FilesProcessed} arquivo(s), {ChunksIndexed} trecho(s), {Errors} erro(s)",
                stats.FilesProcessed, stats.ChunksIndexed, stats.Errors.Count);
        }

        _engineProvider.InvalidateVectorCache();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
