namespace QuestResume.Core.Rag.Evaluation;

/// <summary>
/// A single question in a "golden set" used by <see cref="RagEvaluationHarness"/> to evaluate a
/// <see cref="RagQueryEngine"/> offline. Loadable from a JSON array via
/// <see cref="RagEvaluationHarness.LoadGoldenSet"/>.
/// </summary>
public sealed class EvaluationCase
{
    /// <summary>The question to ask the engine under evaluation.</summary>
    public required string Question { get; init; }

    /// <summary>
    /// Source file paths (or path suffixes, e.g. just the file name) expected to appear among
    /// the retrieved chunks' <see cref="Models.SearchResultItem.SourcePath"/>. Used to compute
    /// recall@k. Empty means "no expectation" — the case's recall@k is reported as 1.0.
    /// </summary>
    public List<string> ExpectedSourcePaths { get; init; } = new();

    /// <summary>
    /// Optional keywords/substrings (case-insensitive); the case passes the answer-content check
    /// when at least one is found in the generated answer. Null/empty skips this check entirely
    /// for the case (its <see cref="RagEvaluationCaseReport.AnswerContainsMatch"/> is <c>null</c>).
    /// </summary>
    public List<string>? ExpectedAnswerContains { get; init; }
}
