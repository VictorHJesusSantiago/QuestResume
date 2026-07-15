namespace QuestResume.Core.Rag.Evaluation;

/// <summary>Per-case result produced by <see cref="RagEvaluationHarness.RunAsync"/>.</summary>
public sealed class RagEvaluationCaseReport
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

    /// <summary>
    /// Fraction (0-1) of <see cref="EvaluationCase.ExpectedSourcePaths"/> found among the
    /// retrieved sources' <see cref="Models.SearchResultItem.SourcePath"/>. 1.0 when the case
    /// declared no expected paths.
    /// </summary>
    public required double RecallAtK { get; init; }

    /// <summary>
    /// Whether the generated answer contains at least one of
    /// <see cref="EvaluationCase.ExpectedAnswerContains"/> (case-insensitive substring match), or
    /// <c>null</c> when the case didn't declare any keywords to check.
    /// </summary>
    public bool? AnswerContainsMatch { get; init; }

    public IReadOnlyList<string> RetrievedSourcePaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MissingExpectedSourcePaths { get; init; } = Array.Empty<string>();
}

/// <summary>Aggregated report over an entire golden set, produced by <see cref="RagEvaluationHarness.RunAsync"/>.</summary>
public sealed class RagEvaluationReport
{
    public required IReadOnlyList<RagEvaluationCaseReport> CaseReports { get; init; }

    public int CaseCount { get; init; }

    /// <summary>Mean of every case's <see cref="RagEvaluationCaseReport.RecallAtK"/>.</summary>
    public double AverageRecallAtK { get; init; }

    /// <summary>
    /// Fraction of cases that declared <see cref="EvaluationCase.ExpectedAnswerContains"/> and
    /// passed the check, or <c>null</c> when no case in the set declared any keywords.
    /// </summary>
    public double? AnswerContainsMatchRate { get; init; }
}
