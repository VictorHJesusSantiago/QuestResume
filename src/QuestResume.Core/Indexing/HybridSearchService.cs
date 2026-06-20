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
    /// <summary>
    /// When a cross-encoder is configured, how many times <paramref name="topK"/> candidates to
    /// retrieve from BM25/vector search before re-ranking and trimming back down to
    /// <paramref name="topK"/> — gives the cross-encoder a wider pool to pick the best matches
    /// from than the raw BM25/vector ranking alone.
    /// </summary>
    private const int RerankCandidateMultiplier = 4;

    private readonly ISearchService _searchService;
    private readonly IVectorStore? _vectorStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly double _bm25Weight;
    private readonly ICrossEncoderService? _crossEncoder;

    public HybridSearchService(
        ISearchService searchService,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null,
        double bm25Weight = 0.5,
        ICrossEncoderService? crossEncoder = null)
    {
        _searchService = searchService;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _bm25Weight = bm25Weight;
        _crossEncoder = crossEncoder;
    }

    /// <summary>
    /// Returns every indexed chunk for the given source file path, ordered by chunk index.
    /// Bypasses the hybrid BM25/vector ranking entirely — used when the caller wants a whole
    /// document's content (e.g. <see cref="QuestResume.Core.Rag.RagQueryEngine.CompareAsync"/>),
    /// not chunks relevant to a query.
    /// </summary>
    public IReadOnlyList<SearchResultItem> GetChunksByPath(string path) => _searchService.GetChunksByPath(path);

    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken = default)
    {
        var candidateK = _crossEncoder is not null ? topK * RerankCandidateMultiplier : topK;

        var bm25Results = _searchService.Search(queryText, candidateK);

        IReadOnlyList<SearchResultItem> combined;
        if (_vectorStore is null || _embeddingService is null)
        {
            combined = bm25Results;
        }
        else
        {
            IReadOnlyList<SearchResultItem> vectorResults;
            try
            {
                var queryEmbedding = await _embeddingService.EmbedAsync(queryText, cancellationToken).ConfigureAwait(false);
                vectorResults = _vectorStore.Search(queryEmbedding, candidateK);
                combined = Combine(bm25Results, vectorResults, candidateK);
            }
            catch (EmbeddingsNotConfiguredException)
            {
                combined = bm25Results;
            }
        }

        return await ApplyRerankingAsync(queryText, combined, topK, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-scores each of <paramref name="candidates"/> against <paramref name="queryText"/>
    /// using the configured <see cref="CrossEncoderService"/> and returns the top
    /// <paramref name="topK"/> by that score. Falls back to the existing BM25/vector ordering
    /// (just trimmed to <paramref name="topK"/>) when no cross-encoder is configured, or if it
    /// isn't usable at query time.
    /// </summary>
    private async Task<IReadOnlyList<SearchResultItem>> ApplyRerankingAsync(
        string queryText, IReadOnlyList<SearchResultItem> candidates, int topK, CancellationToken cancellationToken)
    {
        if (_crossEncoder is null || candidates.Count == 0)
        {
            return candidates.Take(topK).ToList();
        }

        try
        {
            var scored = new List<(SearchResultItem Item, float Score)>(candidates.Count);
            foreach (var item in candidates)
            {
                var score = await _crossEncoder.ScoreAsync(queryText, item.ChunkText, cancellationToken).ConfigureAwait(false);
                scored.Add((item, score));
            }

            return scored
                .OrderByDescending(s => s.Score)
                .Take(topK)
                .Select(s => s.Item)
                .ToList();
        }
        catch (RerankingNotConfiguredException)
        {
            return candidates.Take(topK).ToList();
        }
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
