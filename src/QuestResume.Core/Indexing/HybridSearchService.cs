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
    private readonly bool _useRrf;
    private readonly int _rrfK;
    private readonly Func<CancellationToken, Task<QuestResume.Core.Rag.ILlmProvider>>? _llmFactory;
    private readonly bool _queryExpansionEnabled;
    private readonly bool _hydeEnabled;
    private readonly bool _multiQueryEnabled;
    private readonly int _multiQueryVariations;

    public HybridSearchService(
        ISearchService searchService,
        IVectorStore? vectorStore = null,
        IEmbeddingService? embeddingService = null,
        double bm25Weight = 0.5,
        ICrossEncoderService? crossEncoder = null,
        string rankFusionStrategy = "Linear",
        int rrfK = 60,
        Func<CancellationToken, Task<QuestResume.Core.Rag.ILlmProvider>>? llmFactory = null,
        bool queryExpansionEnabled = false,
        bool hydeEnabled = false,
        bool multiQueryEnabled = false,
        int multiQueryVariations = 3)
    {
        _searchService = searchService;
        _vectorStore = vectorStore;
        _embeddingService = embeddingService;
        _bm25Weight = bm25Weight;
        _crossEncoder = crossEncoder;
        _useRrf = string.Equals(rankFusionStrategy, "Rrf", StringComparison.OrdinalIgnoreCase);
        _rrfK = rrfK > 0 ? rrfK : 60;
        _llmFactory = llmFactory;
        _queryExpansionEnabled = queryExpansionEnabled;
        _hydeEnabled = hydeEnabled;
        _multiQueryEnabled = multiQueryEnabled;
        _multiQueryVariations = multiQueryVariations > 0 ? multiQueryVariations : 3;
    }

    /// <summary>
    /// Returns every indexed chunk for the given source file path, ordered by chunk index.
    /// Bypasses the hybrid BM25/vector ranking entirely — used when the caller wants a whole
    /// document's content (e.g. <see cref="QuestResume.Core.Rag.RagQueryEngine.CompareAsync"/>),
    /// not chunks relevant to a query.
    /// </summary>
    public IReadOnlyList<SearchResultItem> GetChunksByPath(string path) => _searchService.GetChunksByPath(path);

    /// <summary>
    /// Runs hybrid retrieval for <paramref name="queryText"/>, optionally enhanced by LLM-backed
    /// query-quality features (all opt-in and best-effort — an LLM failure at any stage silently
    /// falls back to the corresponding non-enhanced behaviour, never breaking the search):
    /// <list type="bullet">
    /// <item>Query expansion (<see cref="Configuration.AppOptions.QueryExpansionEnabled"/>): 2-3
    /// LLM-suggested related terms are OR'd into the BM25 query text.</item>
    /// <item>HyDE (<see cref="Configuration.AppOptions.HydeEnabled"/>): the vector search embeds
    /// an LLM-generated hypothetical answer instead of the raw question.</item>
    /// <item>Multi-query (<see cref="Configuration.AppOptions.MultiQueryEnabled"/>): retrieval is
    /// run once per LLM-generated question variation (plus the original), and the resulting
    /// ranked lists are merged via Reciprocal Rank Fusion (see <see cref="CombineRrf"/>)
    /// regardless of <see cref="Configuration.AppOptions.RankFusionStrategy"/> — RRF is a natural
    /// fit for merging an arbitrary number of ranked lists, whereas the linear combination is
    /// only meaningful for exactly two (BM25 + vector).</item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<SearchResultItem>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken = default)
    {
        var candidateK = _crossEncoder is not null ? topK * RerankCandidateMultiplier : topK;

        var enhancementsRequested = _queryExpansionEnabled || _hydeEnabled || _multiQueryEnabled;
        QuestResume.Core.Rag.ILlmProvider? llm = null;
        if (enhancementsRequested && _llmFactory is not null)
        {
            try
            {
                llm = await _llmFactory(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                llm = null; // Best-effort: LLM unavailable, all enhancements below are skipped.
            }
        }

        IReadOnlyList<string>? extraTerms = null;
        string? hydeText = null;
        var queryVariations = new List<string> { queryText };

        if (llm is not null)
        {
            var enhancer = new QuestResume.Core.Rag.QueryEnhancementService(llm);

            if (_queryExpansionEnabled)
            {
                try
                {
                    extraTerms = await enhancer.ExpandQueryAsync(queryText, cancellationToken).ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }

            if (_hydeEnabled && _vectorStore is not null && _embeddingService is not null)
            {
                try
                {
                    hydeText = await enhancer.GenerateHypotheticalAnswerAsync(queryText, cancellationToken).ConfigureAwait(false);
                }
                catch { /* best-effort */ }
            }

            if (_multiQueryEnabled)
            {
                try
                {
                    var variations = await enhancer.GenerateQueryVariationsAsync(queryText, _multiQueryVariations, cancellationToken).ConfigureAwait(false);
                    queryVariations.AddRange(variations);
                }
                catch { /* best-effort */ }
            }
        }

        IReadOnlyList<SearchResultItem> combined;
        if (queryVariations.Count > 1)
        {
            var perVariationLists = new List<IReadOnlyList<SearchResultItem>>(queryVariations.Count);
            foreach (var variation in queryVariations)
            {
                var isOriginal = ReferenceEquals(variation, queryText) || variation == queryText;
                perVariationLists.Add(await SearchSingleVariationAsync(
                    variation, extraTerms, isOriginal ? hydeText : null, candidateK, cancellationToken).ConfigureAwait(false));
            }

            combined = CombineRrf(perVariationLists, candidateK);
        }
        else
        {
            combined = await SearchSingleVariationAsync(queryText, extraTerms, hydeText, candidateK, cancellationToken).ConfigureAwait(false);
        }

        return await ApplyRerankingAsync(queryText, combined, topK, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs BM25 + (optional) vector retrieval for a single query variation and combines them
    /// using the configured <see cref="Configuration.AppOptions.RankFusionStrategy"/> — the same
    /// per-variation building block used both for a plain single-query search and for each
    /// variation in multi-query retrieval.
    /// </summary>
    private async Task<IReadOnlyList<SearchResultItem>> SearchSingleVariationAsync(
        string queryText, IReadOnlyList<string>? extraTerms, string? hydeText, int candidateK, CancellationToken cancellationToken)
    {
        var bm25QueryText = extraTerms is { Count: > 0 }
            ? $"{queryText} {string.Join(' ', extraTerms.Select(t => $"OR {t}"))}"
            : queryText;

        var bm25Results = _searchService.Search(bm25QueryText, candidateK);

        if (_vectorStore is null || _embeddingService is null)
        {
            return bm25Results;
        }

        try
        {
            var textForEmbedding = !string.IsNullOrWhiteSpace(hydeText) ? hydeText! : queryText;
            var queryEmbedding = await _embeddingService.EmbedAsync(textForEmbedding, cancellationToken).ConfigureAwait(false);
            var vectorResults = _vectorStore.Search(queryEmbedding, candidateK);
            return Combine(bm25Results, vectorResults, candidateK);
        }
        catch (EmbeddingsNotConfiguredException)
        {
            return bm25Results;
        }
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
        if (_useRrf)
        {
            return CombineRrf(new[] { bm25Results, vectorResults }, topK);
        }

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
    /// Reciprocal Rank Fusion (<see cref="Configuration.AppOptions.RankFusionStrategy"/> = "Rrf"):
    /// for each result, <c>score_rrf = sum(1 / (k + rank_in_list))</c> across every ranked list it
    /// appears in (BM25, vector, and — via <see cref="SearchWithVariationsAsync"/> — one list per
    /// multi-query variation). Rank is 1-based within each list. Unlike the linear combination,
    /// RRF doesn't need score normalization since it only depends on rank order.
    /// </summary>
    public IReadOnlyList<SearchResultItem> CombineRrf(IEnumerable<IReadOnlyList<SearchResultItem>> resultLists, int topK)
    {
        var scores = new Dictionary<(string SourcePath, int ChunkIndex), (SearchResultItem Item, double Score)>();

        foreach (var list in resultLists)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var key = (item.SourcePath, item.ChunkIndex);
                var contribution = 1.0 / (_rrfK + i + 1);

                scores[key] = scores.TryGetValue(key, out var existing)
                    ? (existing.Item, existing.Score + contribution)
                    : (item, contribution);
            }
        }

        return scores.Values
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
