using QuestResume.Core.Indexing;
using QuestResume.Core.Rag;
using QuestResume.Core.Rag.Evaluation;

namespace QuestResume.Core.Tests;

public class RagEvaluationHarnessTests
{
    [Fact]
    public void LoadGoldenSet_ValidJson_ParsesCases()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-set-{Guid.NewGuid()}.json");
        File.WriteAllText(path, """
            [
              { "question": "O que é X?", "expectedSourcePaths": ["docs/x.txt"], "expectedAnswerContains": ["x é"] },
              { "question": "O que é Y?" }
            ]
            """);

        try
        {
            var cases = RagEvaluationHarness.LoadGoldenSet(path);

            Assert.Equal(2, cases.Count);
            Assert.Equal("O que é X?", cases[0].Question);
            Assert.Equal(new[] { "docs/x.txt" }, cases[0].ExpectedSourcePaths);
            Assert.Equal(new[] { "x é" }, cases[0].ExpectedAnswerContains);
            Assert.Equal("O que é Y?", cases[1].Question);
            Assert.Empty(cases[1].ExpectedSourcePaths);
            Assert.Null(cases[1].ExpectedAnswerContains);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadGoldenSet_EmptyArray_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"golden-set-empty-{Guid.NewGuid()}.json");
        File.WriteAllText(path, "[]");

        try
        {
            Assert.Throws<InvalidOperationException>(() => RagEvaluationHarness.LoadGoldenSet(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ScoreCase_AllExpectedSourcesRetrieved_RecallIsOne()
    {
        var evalCase = new EvaluationCase { Question = "q", ExpectedSourcePaths = new List<string> { "a.txt", "b.txt" } };
        var result = new QuestResume.Core.Models.AskResult
        {
            Answer = "resposta",
            Sources = new[] { MakeSource("a.txt"), MakeSource("b.txt"), MakeSource("c.txt") }
        };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.Equal(1.0, report.RecallAtK, precision: 5);
        Assert.Empty(report.MissingExpectedSourcePaths);
    }

    [Fact]
    public void ScoreCase_PartialSourcesRetrieved_RecallReflectsMissingFraction()
    {
        var evalCase = new EvaluationCase { Question = "q", ExpectedSourcePaths = new List<string> { "a.txt", "b.txt" } };
        var result = new QuestResume.Core.Models.AskResult
        {
            Answer = "resposta",
            Sources = new[] { MakeSource("a.txt") }
        };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.Equal(0.5, report.RecallAtK, precision: 5);
        Assert.Equal(new[] { "b.txt" }, report.MissingExpectedSourcePaths);
    }

    [Fact]
    public void ScoreCase_NoExpectedSources_RecallIsOneTrivially()
    {
        var evalCase = new EvaluationCase { Question = "q" };
        var result = new QuestResume.Core.Models.AskResult { Answer = "resposta", Sources = Array.Empty<QuestResume.Core.Models.SearchResultItem>() };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.Equal(1.0, report.RecallAtK, precision: 5);
    }

    [Fact]
    public void ScoreCase_ExpectedAnswerContains_MatchesCaseInsensitively()
    {
        var evalCase = new EvaluationCase { Question = "q", ExpectedAnswerContains = new List<string> { "PALAVRA-CHAVE" } };
        var result = new QuestResume.Core.Models.AskResult { Answer = "A resposta contém a palavra-chave esperada.", Sources = Array.Empty<QuestResume.Core.Models.SearchResultItem>() };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.True(report.AnswerContainsMatch);
    }

    [Fact]
    public void ScoreCase_ExpectedAnswerContainsAbsent_AnswerContainsMatchIsFalse()
    {
        var evalCase = new EvaluationCase { Question = "q", ExpectedAnswerContains = new List<string> { "não presente" } };
        var result = new QuestResume.Core.Models.AskResult { Answer = "Resposta qualquer.", Sources = Array.Empty<QuestResume.Core.Models.SearchResultItem>() };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.False(report.AnswerContainsMatch);
    }

    [Fact]
    public void ScoreCase_NoExpectedAnswerContains_AnswerContainsMatchIsNull()
    {
        var evalCase = new EvaluationCase { Question = "q" };
        var result = new QuestResume.Core.Models.AskResult { Answer = "Resposta qualquer.", Sources = Array.Empty<QuestResume.Core.Models.SearchResultItem>() };

        var report = RagEvaluationHarness.ScoreCase(evalCase, result);

        Assert.Null(report.AnswerContainsMatch);
    }

    [Fact]
    public async Task RunAsync_DrivesRealEngineEndToEndAndAggregatesReport()
    {
        var (folder, indexPath) = await CreateIndexedFolderAsync();

        try
        {
            var search = new SearchService(indexPath);
            var llm = new FakeLlmProvider(_ => "QuestResume é um sistema de RAG offline.");

            using var engine = new RagQueryEngine(search, modelPath: string.Empty, llmProviderOverride: llm);

            var cases = new List<EvaluationCase>
            {
                new() { Question = "O que é QuestResume?", ExpectedSourcePaths = new List<string> { "doc.txt" }, ExpectedAnswerContains = new List<string> { "RAG" } },
                new() { Question = "Outra pergunta sobre o mesmo conteúdo?" }
            };

            var report = await RagEvaluationHarness.RunAsync(engine, cases);

            Assert.Equal(2, report.CaseCount);
            Assert.Equal(2, report.CaseReports.Count);
            Assert.True(report.AverageRecallAtK > 0);
            Assert.NotNull(report.AnswerContainsMatchRate);
            Assert.Equal(1.0, report.AnswerContainsMatchRate!.Value, precision: 5);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    private static QuestResume.Core.Models.SearchResultItem MakeSource(string path) => new()
    {
        SourcePath = path,
        FileName = Path.GetFileName(path),
        ChunkIndex = 0,
        ChunkText = "texto",
        Score = 5
    };

    private static async Task<(string Folder, string IndexPath)> CreateIndexedFolderAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"eval-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"eval-index-{Guid.NewGuid()}");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "doc.txt"), "QuestResume é um sistema de RAG offline para busca em documentos locais.");

        var indexer = new DocumentIndexer();
        await indexer.IndexFolderAsync(folder, indexPath);

        return (folder, indexPath);
    }
}
