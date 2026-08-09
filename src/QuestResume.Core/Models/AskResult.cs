namespace QuestResume.Core.Models;

public sealed class AskResult
{
    public required string Answer { get; init; }

    public required IReadOnlyList<SearchResultItem> Sources { get; init; }

        public IReadOnlyList<string> RelatedQuestions { get; init; } = Array.Empty<string>();

        public bool? IsFaithful { get; init; }

        public double? ConfidenceScore { get; init; }
}
