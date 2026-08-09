using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Rag.Evaluation;

public static class RagEvaluationHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        private static bool PathsMatch(string retrieved, string expected)
    {
        var normalizedRetrieved = retrieved.Replace('\\', '/');
        var normalizedExpected = expected.Replace('\\', '/');

        return string.Equals(normalizedRetrieved, normalizedExpected, StringComparison.OrdinalIgnoreCase)
            || normalizedRetrieved.EndsWith(normalizedExpected, StringComparison.OrdinalIgnoreCase)
            || normalizedExpected.EndsWith(normalizedRetrieved, StringComparison.OrdinalIgnoreCase);
    }
}
