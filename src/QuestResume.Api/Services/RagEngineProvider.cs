using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;

namespace QuestResume.Api.Services;

/// <summary>
/// Keeps a single <see cref="RagQueryEngine"/> (and the GGUF model it loads) alive across
/// requests, recreating it only when the configured model or index path changes.
/// Loading a multi-gigabyte model on every request would be far too slow.
/// </summary>
public sealed class RagEngineProvider : IDisposable
{
    private readonly object _lock = new();
    private RagQueryEngine? _engine;
    private string? _loadedModelPath;
    private string? _loadedIndexPath;

    public RagQueryEngine GetEngine(AppOptions options)
    {
        lock (_lock)
        {
            if (_engine is null || _loadedModelPath != options.ModelPath || _loadedIndexPath != options.IndexPath)
            {
                _engine?.Dispose();

                var search = new SearchService(options.IndexPath);
                _engine = new RagQueryEngine(search, options.ModelPath, options.ContextSize, options.TopK);
                _loadedModelPath = options.ModelPath;
                _loadedIndexPath = options.IndexPath;
            }

            return _engine;
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}
