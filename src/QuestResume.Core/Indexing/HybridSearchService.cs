using QuestResume.Core.Embeddings;
using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Combines BM25 full-text search (<see cref="SearchService"/>) with vector similarity
/// search (<see cref="VectorStore"/>) when embeddings are configured. Falls back to pure
/// BM25 when <paramref name="vectorStore"/>/<paramref name="embeddingService"/> are not
/// provided, or when embeddings are not configured at query time.
/// </summary>
public sealed class HybridSearchService
{
    private readonly SearchService _searchService;
    private readonly VectorStore? _vectorStore;
    private readonly EmbeddingService? _embeddingService;
    private readonly double _bm25Weight;

    public HybridSearchService(
        SearchService searchService,
        VectorStore? vectorStore = null,
        EmbeddingService? embeddingService = null,
        double bm25Weight = 0.5)
    {
        _searchService = searchService;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _bm25Weight = bm25Weight;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken = default)
    {
        var bm25Results = _searchService.Search(queryText, topK);

        if (_vectorStore is null || _embeddingService is null)
        {
            return bm25Results;
        }

        IReadOnlyList<SearchResultItem> vectorResults;
        try
        {
            var queryEmbedding = await _embeddingService.EmbedAsync(queryText, cancellationToken).ConfigureAwait(false);
            vectorResults = _vectorStore.Search(queryEmbedding, topK);
        }
        catch (EmbeddingsNotConfiguredException)
        {
            return bm25Results;
        }

        return Combine(bm25Results, vectorResults, topK);
    }

    private IReadOnlyList<SearchResultItem> Combine(
        IReadOnlyList<SearchResultItem> bm25Results,
        IReadOnlyList<SearchResultItem> vectorResults,
        int topK)
    {
        var bm25Scores = NormalizeScores(bm25Results);
        var vectorScores = NormalizeScores(vectorResults);

        var combined = new Dictionary<(string SourcePath, int ChunkIndex), (SearchResultItem Item, double Score)>();

        foreach (var item in bm25Results)
        {
            var key = (item.SourcePath, item.ChunkIndex);
            combined[key] = (item, _bm25Weight * bm25Scores[key]);
        }

        foreach (var item in vectorResults)
        {
            var key = (item.SourcePath, item.ChunkIndex);
            var vectorScore = (1 - _bm25Weight) * vectorScores[key];

            combined[key] = combined.TryGetValue(key, out var existing)
                ? (existing.Item, existing.Score + vectorScore)
                : (item, vectorScore);
        }

        return combined.Values
            .OrderByDescending(entry => entry.Score)
            .Take(topK)
            .Select(entry => entry.Item)
            .ToList();
    }

    /// <summary>
    /// Keyed by <c>(SourcePath, ChunkIndex)</c> rather than the <see cref="SearchResultItem"/>
    /// instance itself — <see cref="SearchResultItem"/> doesn't override
    /// <see cref="object.Equals(object?)"/>/<see cref="object.GetHashCode"/>, so a
    /// reference-identity dictionary would silently break (KeyNotFoundException) if either
    /// result list were ever rebuilt/cloned instead of reusing the same instances.
    /// </summary>
    private static Dictionary<(string SourcePath, int ChunkIndex), double> NormalizeScores(IReadOnlyList<SearchResultItem> items)
    {
        var normalized = new Dictionary<(string SourcePath, int ChunkIndex), double>();
        if (items.Count == 0)
        {
            return normalized;
        }

        var min = items.Min(item => item.Score);
        var max = items.Max(item => item.Score);
        var range = max - min;

        foreach (var item in items)
        {
            normalized[(item.SourcePath, item.ChunkIndex)] = range > 0 ? (item.Score - min) / range : 1.0;
        }

        return normalized;
    }
}
