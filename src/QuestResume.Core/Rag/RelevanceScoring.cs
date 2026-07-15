using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

/// <summary>
/// Heuristics shared by the "não sei" guardrail (<see cref="Configuration.AppOptions.MinRelevanceThreshold"/>)
/// and the confidence badge (<see cref="AskResult.ConfidenceScore"/>) to turn the raw, unbounded
/// relevance scores produced by <see cref="Indexing.SearchService"/>/<see cref="Indexing.HybridSearchService"/>
/// (BM25 scores, cosine similarities, RRF scores, cross-encoder scores — all on different scales)
/// into a single comparable 0-1 number.
/// </summary>
public static class RelevanceScoring
{
    /// <summary>
    /// Maps a single raw <see cref="SearchResultItem.Score"/> to a 0-1 range. Cosine-similarity
    /// and RRF-style scores are typically already within (or close to) [0, 1]; raw BM25 scores
    /// are unbounded above but rarely exceed ~10 for a reasonably specific query, so scores are
    /// clamped against that ceiling. This is intentionally a rough heuristic, not a calibrated
    /// probability.
    /// </summary>
    public static double NormalizeScore(double rawScore)
    {
        if (rawScore <= 0)
        {
            return 0;
        }

        return Math.Min(1.0, rawScore / 10.0);
    }

    /// <summary>
    /// Average normalized relevance of <paramref name="sources"/>, or <c>0</c> when there are no
    /// sources (an empty retrieval is treated as "no relevance" for guardrail purposes).
    /// </summary>
    public static double AverageNormalizedScore(IReadOnlyList<SearchResultItem> sources)
    {
        if (sources.Count == 0)
        {
            return 0;
        }

        return sources.Average(s => NormalizeScore(s.Score));
    }

    /// <summary>
    /// Combines the average retrieval relevance with the optional faithfulness verdict into the
    /// single 0-1 <see cref="AskResult.ConfidenceScore"/>. When a faithfulness verdict is
    /// available it counts for 40% of the score (a hallucinated answer should visibly lower
    /// confidence even if the retrieved chunks looked relevant); otherwise the score is just the
    /// average relevance.
    /// </summary>
    public static double? ComputeConfidenceScore(IReadOnlyList<SearchResultItem> sources, bool? isFaithful)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        var relevance = AverageNormalizedScore(sources);

        if (isFaithful is null)
        {
            return relevance;
        }

        var faithfulness = isFaithful.Value ? 1.0 : 0.0;
        return (relevance * 0.6) + (faithfulness * 0.4);
    }
}
