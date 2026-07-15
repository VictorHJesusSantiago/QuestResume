using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag.Evaluation;

/// <summary>
/// Offline evaluation harness for a <see cref="RagQueryEngine"/>: runs every question from a
/// "golden set" (<see cref="EvaluationCase"/>) through <see cref="RagQueryEngine.AskAsync"/> and
/// scores recall@k of the expected sources plus an optional keyword check on the answer. Used by
/// the <c>evaluate</c> CLI command; a dev/CI tool, not exposed in the API or web UI.
/// </summary>
public static class RagEvaluationHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Loads a golden set (JSON array of <see cref="EvaluationCase"/>) from disk.</summary>
    /// <exception cref="InvalidOperationException">When the file is empty, malformed, or not a JSON array of cases.</exception>
    public static List<EvaluationCase> LoadGoldenSet(string path)
    {
        var json = File.ReadAllText(path);
        var cases = JsonSerializer.Deserialize<List<EvaluationCase>>(json, JsonOptions);

        if (cases is null || cases.Count == 0)
        {
            throw new InvalidOperationException($"Golden set vazio ou inválido: {path}");
        }

        return cases;
    }

    /// <summary>
    /// Runs every case in <paramref name="cases"/> against <paramref name="engine"/> sequentially
    /// (mirrors how <c>ask-batch</c> drives a shared engine) and returns the aggregated report.
    /// </summary>
    public static async Task<RagEvaluationReport> RunAsync(
        RagQueryEngine engine,
        IReadOnlyList<EvaluationCase> cases,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        var caseReports = new List<RagEvaluationCaseReport>(cases.Count);

        foreach (var evalCase in cases)
        {
            var result = await engine.AskAsync(evalCase.Question, topK, cancellationToken: cancellationToken).ConfigureAwait(false);
            caseReports.Add(ScoreCase(evalCase, result));
        }

        return Aggregate(caseReports);
    }

    /// <summary>Scores a single case's already-computed <see cref="AskResult"/> (used by <see cref="RunAsync"/>; exposed for direct unit testing).</summary>
    internal static RagEvaluationCaseReport ScoreCase(EvaluationCase evalCase, AskResult result)
    {
        var retrievedPaths = result.Sources.Select(s => s.SourcePath).Distinct().ToList();

        double recallAtK;
        List<string> missing;
        if (evalCase.ExpectedSourcePaths.Count == 0)
        {
            recallAtK = 1.0;
            missing = new List<string>();
        }
        else
        {
            missing = evalCase.ExpectedSourcePaths.Where(expected => !retrievedPaths.Any(retrieved => PathsMatch(retrieved, expected))).ToList();
            recallAtK = (double)(evalCase.ExpectedSourcePaths.Count - missing.Count) / evalCase.ExpectedSourcePaths.Count;
        }

        bool? answerContainsMatch = null;
        if (evalCase.ExpectedAnswerContains is { Count: > 0 } keywords)
        {
            answerContainsMatch = keywords.Any(keyword => result.Answer.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        return new RagEvaluationCaseReport
        {
            Question = evalCase.Question,
            Answer = result.Answer,
            RecallAtK = recallAtK,
            AnswerContainsMatch = answerContainsMatch,
            RetrievedSourcePaths = retrievedPaths,
            MissingExpectedSourcePaths = missing
        };
    }

    private static RagEvaluationReport Aggregate(IReadOnlyList<RagEvaluationCaseReport> caseReports)
    {
        var averageRecall = caseReports.Count > 0 ? caseReports.Average(r => r.RecallAtK) : 0;

        var withAnswerCheck = caseReports.Where(r => r.AnswerContainsMatch.HasValue).ToList();
        double? answerContainsMatchRate = withAnswerCheck.Count > 0
            ? withAnswerCheck.Count(r => r.AnswerContainsMatch!.Value) / (double)withAnswerCheck.Count
            : null;

        return new RagEvaluationReport
        {
            CaseReports = caseReports,
            CaseCount = caseReports.Count,
            AverageRecallAtK = averageRecall,
            AnswerContainsMatchRate = answerContainsMatchRate
        };
    }

    /// <summary>
    /// Two source paths are considered a match when equal (case-insensitive) or when one ends
    /// with the other after normalizing slashes — lets a golden set reference just a file name
    /// (e.g. <c>"contrato.pdf"</c>) instead of a full, environment-specific absolute path.
    /// </summary>
    private static bool PathsMatch(string retrieved, string expected)
    {
        var normalizedRetrieved = retrieved.Replace('\\', '/');
        var normalizedExpected = expected.Replace('\\', '/');

        return string.Equals(normalizedRetrieved, normalizedExpected, StringComparison.OrdinalIgnoreCase)
            || normalizedRetrieved.EndsWith(normalizedExpected, StringComparison.OrdinalIgnoreCase)
            || normalizedExpected.EndsWith(normalizedRetrieved, StringComparison.OrdinalIgnoreCase);
    }
}
