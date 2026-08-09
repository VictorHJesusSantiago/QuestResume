using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

public static class RelevanceScoring
{
        public static double NormalizeScore(double rawScore)
    {
        if (rawScore <= 0)
        {
            return 0;
        }

        return Math.Min(1.0, rawScore / 10.0);
    }

        public static double AverageNormalizedScore(IReadOnlyList<SearchResultItem> sources)
    {
        if (sources.Count == 0)
        {
            return 0;
        }

        return sources.Average(s => NormalizeScore(s.Score));
    }

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
