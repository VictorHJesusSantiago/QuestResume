namespace QuestResume.Core.Rag.Evaluation;

public sealed class RagEvaluationCaseReport
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

        public required double RecallAtK { get; init; }

        public bool? AnswerContainsMatch { get; init; }

    public IReadOnlyList<string> RetrievedSourcePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MissingExpectedSourcePaths { get; init; } = Array.Empty<string>();
}

public sealed class RagEvaluationReport
{
    public required IReadOnlyList<RagEvaluationCaseReport> CaseReports { get; init; }

    public int CaseCount { get; init; }

        public double AverageRecallAtK { get; init; }

        public double? AnswerContainsMatchRate { get; init; }
}
