using QuestResume.Core.Models;
using QuestResume.Core.Rag;

namespace QuestResume.Core.Tests;

public class RelevanceScoringTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(5, 0.5)]
    [InlineData(10, 1.0)]
    [InlineData(50, 1.0)]
    public void NormalizeScore_ClampsToZeroOneRange(double raw, double expected)
    {
        Assert.Equal(expected, RelevanceScoring.NormalizeScore(raw), precision: 5);
    }

    [Fact]
    public void AverageNormalizedScore_NoSources_ReturnsZero()
    {
        Assert.Equal(0, RelevanceScoring.AverageNormalizedScore(Array.Empty<SearchResultItem>()));
    }

    [Fact]
    public void AverageNormalizedScore_AveragesNormalizedScoresAcrossSources()
    {
        var sources = new[]
        {
            MakeSource(score: 10), // normalizes to 1.0
            MakeSource(score: 0)   // normalizes to 0.0
        };

        Assert.Equal(0.5, RelevanceScoring.AverageNormalizedScore(sources), precision: 5);
    }

    [Fact]
    public void ComputeConfidenceScore_NoSources_ReturnsNull()
    {
        Assert.Null(RelevanceScoring.ComputeConfidenceScore(Array.Empty<SearchResultItem>(), isFaithful: null));
    }

    [Fact]
    public void ComputeConfidenceScore_WithoutFaithfulnessVerdict_EqualsAverageRelevance()
    {
        var sources = new[] { MakeSource(score: 10) };
        Assert.Equal(1.0, RelevanceScoring.ComputeConfidenceScore(sources, isFaithful: null)!.Value, precision: 5);
    }

    [Fact]
    public void ComputeConfidenceScore_FaithfulTrue_BlendsInFavorOfHigherConfidence()
    {
        var sources = new[] { MakeSource(score: 0) }; // relevance = 0
        var confidence = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful: true);
        // relevance(0) * 0.6 + faithfulness(1) * 0.4 = 0.4
        Assert.Equal(0.4, confidence!.Value, precision: 5);
    }

    [Fact]
    public void ComputeConfidenceScore_FaithfulFalse_LowersConfidenceEvenWithHighRelevance()
    {
        var sources = new[] { MakeSource(score: 10) }; // relevance = 1.0
        var confidence = RelevanceScoring.ComputeConfidenceScore(sources, isFaithful: false);
        // relevance(1.0) * 0.6 + faithfulness(0) * 0.4 = 0.6
        Assert.Equal(0.6, confidence!.Value, precision: 5);
    }

    private static SearchResultItem MakeSource(float score) => new()
    {
        SourcePath = "doc.txt",
        FileName = "doc.txt",
        ChunkIndex = 0,
        ChunkText = "texto",
        Score = score
    };
}
