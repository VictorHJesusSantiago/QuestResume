using System.Text.Json;
using QuestResume.Core.Configuration;
using QuestResume.Core.Indexing;
using QuestResume.Core.Models;
using QuestResume.Core.Persistence;
using QuestResume.Core.Rag;
using Xunit;

namespace QuestResume.Core.Tests;

public sealed class NewStudyAndAnalysisFeaturesTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "qr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static IReadOnlyList<SearchResultItem> Chunks(string path, params string[] texts) =>
        texts.Select((t, i) => new SearchResultItem
        {
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ChunkIndex = i,
            ChunkText = t,
            Score = 1f
        }).ToList();

    // ---- Item 1: Anki export ----
    [Fact]
    public void AnkiExporter_ProducesTabDelimitedLines_AndNeutralizesNewlines()
    {
        var cards = new List<Flashcard>
        {
            new() { Question = "O que é RAG?", Answer = "Retrieval\nAugmented" },
            new() { Question = "Q2", Answer = "A2" }
        };
        var output = AnkiExporter.Export(cards);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("O que é RAG?\tRetrieval<br>Augmented", lines[0]);
        Assert.Equal("Q2\tA2", lines[1]);
    }

    // ---- Item 2: SM-2 ----
    [Fact]
    public void SpacedRepetition_GoodAnswers_IncreaseIntervalAndRepetitions()
    {
        var card = new SpacedRepetitionCard();
        var today = new DateTime(2026, 1, 1);
        SpacedRepetition.Apply(card, 5, today);
        Assert.Equal(1, card.Repetitions);
        Assert.Equal(1, card.Interval);
        SpacedRepetition.Apply(card, 5, today);
        Assert.Equal(2, card.Repetitions);
        Assert.Equal(6, card.Interval);
        SpacedRepetition.Apply(card, 5, today);
        Assert.Equal(3, card.Repetitions);
        Assert.True(card.Interval > 6);
        Assert.Equal(today.AddDays(card.Interval), card.NextReviewDate);
    }

    [Fact]
    public void SpacedRepetition_FailingAnswer_ResetsRepetitions_AndEaseFloor()
    {
        var card = new SpacedRepetitionCard { Repetitions = 5, Interval = 40, EaseFactor = 1.3 };
        SpacedRepetition.Apply(card, 1);
        Assert.Equal(0, card.Repetitions);
        Assert.Equal(1, card.Interval);
        Assert.True(card.EaseFactor >= SpacedRepetition.MinEaseFactor);
    }

    [Fact]
    public void SpacedRepetition_InvalidQuality_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SpacedRepetition.Apply(new SpacedRepetitionCard(), 6));

    // ---- Item 2/3: StudyProgressStore ----
    [Fact]
    public void StudyProgressStore_RecordReview_UpdatesScheduleAndDueCards_AndStats()
    {
        var dir = TempDir();
        var store = new StudyProgressStore(dir);
        var card = store.GetOrCreate("doc.pdf", "Pergunta?");
        Assert.Contains(store.GetDueCards(), c => c.CardId == card.CardId);

        var today = DateTime.UtcNow.Date;
        store.RecordReview(card.CardId, 5, today);
        // Após acerto o card sai da lista de vencidos de hoje.
        Assert.DoesNotContain(store.GetDueCards(today), c => c.CardId == card.CardId);

        var stats = store.ComputeStats(30, today);
        Assert.Equal(1, stats.TotalCards);
        Assert.Equal(1, stats.TotalCorrect);
        Assert.Equal(1.0, stats.OverallAccuracy);
        Assert.Equal(30, stats.Daily.Count);

        // Persistência.
        var reloaded = new StudyProgressStore(dir);
        Assert.Single(reloaded.GetAllCards());
    }

    // ---- Item 9: Knowledge graph ----
    [Fact]
    public void KnowledgeGraph_BuildsCoOccurrenceNodesAndEdges()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>
        {
            ["a.txt"] = new[] { "Ana", "Acme", "Ana" },
            ["b.txt"] = new[] { "Ana", "Beta" }
        };
        var g = KnowledgeGraph.Build(map);
        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal(2, g.Nodes.First(n => n.Label == "Ana").DocumentCount);
        // Ana-Acme co-ocorrem em a.txt; Ana-Beta em b.txt.
        Assert.Contains(g.Edges, e => (e.Source == "Ana" || e.Target == "Ana") && (e.Source == "Acme" || e.Target == "Acme"));
    }

    // ---- Item 20: Document versions + diff ----
    [Fact]
    public void DocumentVersionStore_SavesVersions_AppliesRetention_AndDiffs()
    {
        var dir = TempDir();
        var store = new DocumentVersionStore(dir, maxVersionsPerDocument: 2);
        Assert.True(store.SaveVersion("d.txt", "linha1\nlinha2"));
        Assert.False(store.SaveVersion("d.txt", "linha1\nlinha2")); // hash igual -> ignora
        Assert.True(store.SaveVersion("d.txt", "linha1\nlinha3"));
        Assert.True(store.SaveVersion("d.txt", "linha1\nlinha4"));
        var versions = store.GetVersions("d.txt");
        Assert.Equal(2, versions.Count); // retenção
        Assert.Equal(1, versions[0].VersionNumber);

        var diff = DocumentVersionStore.DiffLines("a\nb\nc", "a\nx\nc");
        Assert.Contains("  a", diff);
        Assert.Contains("- b", diff);
        Assert.Contains("+ x", diff);
    }

    // ---- Item 11: Annotations ----
    [Fact]
    public void AnnotationStore_AddGetRemove_PersistsAndFilters()
    {
        var dir = TempDir();
        var store = new AnnotationStore(dir);
        var ann = store.Add(new Annotation { SourcePath = "d.txt", Text = "trecho", Note = "nota", StartOffset = 0, EndOffset = 6 });
        store.Add(new Annotation { SourcePath = "other.txt", Text = "x", StartOffset = 0, EndOffset = 1 });

        Assert.Single(new AnnotationStore(dir).GetForDocument("d.txt"));
        Assert.True(store.Remove(ann.Id));
        Assert.Empty(new AnnotationStore(dir).GetForDocument("d.txt"));
    }

    // ---- Item 13: Search result export ----
    [Fact]
    public void SearchResultExporter_Csv_EscapesAndHeaders_Xlsx_NonEmpty()
    {
        var results = Chunks("c:/docs/a.txt", "texto com, vírgula e \"aspas\"").ToList();
        var csv = SearchResultExporter.ToCsv(results);
        Assert.StartsWith("Arquivo,Caminho,Trecho", csv);
        Assert.Contains("\"texto com, vírgula e \"\"aspas\"\"\"", csv);

        var xlsx = SearchResultExporter.ToXlsx(results);
        Assert.True(xlsx.Length > 0);
        // PK zip header.
        Assert.Equal(0x50, xlsx[0]);
        Assert.Equal(0x4B, xlsx[1]);
    }

    // ---- Item 12: chat exporters ----
    [Fact]
    public void ChatExporters_ProduceExpectedFormats()
    {
        var turns = new List<ChatExportTurn> { new("P1", "R1", new[] { "a.txt" }) };
        var md = ChatTextExporter.ToMarkdown("Conversa", turns);
        Assert.Contains("# Conversa", md);
        var txt = ChatTextExporter.ToPlainText("Conversa", turns);
        Assert.DoesNotContain("#", txt);
        Assert.Contains("P1", txt);
        var html = ChatHtmlExporter.Export("Conversa", turns);
        Assert.Contains("<html", html);
        Assert.Contains("R1", html);
        var docx = ChatDocxExporter.Export("Conversa", turns);
        Assert.True(docx.Length > 0);
        Assert.Equal(0x50, docx[0]); // zip (docx é OOXML zip)
    }

    // ---- Item 14: Backup scheduler retention ----
    [Fact]
    public async Task BackupScheduler_RunOnce_CreatesBackup_AndAppliesRetention()
    {
        var indexDir = TempDir();
        File.WriteAllText(Path.Combine(indexDir, "segments.gen"), "x");
        var backupDir = TempDir();
        var scheduler = new BackupScheduler(TimeSpan.FromHours(1), indexDir, backupDir, retentionCount: 2);

        for (var i = 0; i < 4; i++)
        {
            // Nomes usam timestamp em segundos; garanta unicidade criando arquivos manualmente.
            var name = $"{BackupScheduler.BackupPrefix}2026010{i}-000000.zip";
            File.WriteAllText(Path.Combine(backupDir, name), "z");
        }
        await scheduler.RunBackupOnceAsync();
        var backups = Directory.GetFiles(backupDir, $"{BackupScheduler.BackupPrefix}*.zip");
        Assert.Equal(2, backups.Length);
    }

    // ---- Item 15: index health check ----
    [Fact]
    public void IndexHealthCheck_MissingIndex_ReportsUnhealthy()
    {
        var dir = TempDir();
        var report = new IndexHealthCheckService().Check(dir);
        Assert.False(report.IsHealthy);
        Assert.False(report.IndexExists);
    }

    // ---- Item 16: collection disk usage ----
    [Fact]
    public void CollectionDiskUsage_SumsFileSizes()
    {
        var baseDir = TempDir();
        var store = new CollectionStore(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "f.bin"), new string('x', 100));
        var usage = new QuestResume.Core.Services.CollectionDiskUsageService(store).Compute();
        Assert.NotEmpty(usage);
        Assert.True(usage.Sum(u => u.SizeBytes) >= 100);
    }

    // ---- Item 17: config export/import ----
    [Fact]
    public void ConfigService_ExportRedactsSecrets_ImportValidatesAndPreservesRedacted()
    {
        var dir = TempDir();
        var configPath = Path.Combine(dir, "config.json");
        var svc = new ConfigService(configPath);
        svc.Save(new AppOptions { IndexPath = Path.Combine(dir, "index"), GoogleDriveClientId = "segredo-123" });

        var exported = svc.ExportConfig();
        Assert.Contains("***REDACTED***", exported);
        Assert.DoesNotContain("segredo-123", exported);
        Assert.Contains("_aviso", exported);

        // Import de volta não deve apagar o segredo (placeholder é ignorado).
        var imported = svc.ImportConfig(exported);
        Assert.Equal("segredo-123", imported.GoogleDriveClientId);
    }

    // ---- Item 8: entity extraction + store ----
    [Fact]
    public async Task EntityExtraction_ParsesJson_AndStoreSupportsReverseLookup()
    {
        var llm = new FakeLlmProvider(_ => "[{\"type\":\"pessoa\",\"name\":\"Ana\"},{\"type\":\"empresa\",\"name\":\"Acme\"}]");
        var svc = new EntityExtractionService(llm);
        var entities = await svc.ExtractAsync("texto qualquer");
        Assert.Equal(2, entities.Count);

        var dir = TempDir();
        var store = new EntityStore(dir);
        store.SetEntities("a.txt", entities);
        Assert.Contains("a.txt", store.GetDocumentsMentioning("ana"));
        Assert.Empty(store.GetDocumentsMentioning("inexistente"));
    }

    // ---- Item 4/5/10: LLM analysis services ----
    [Fact]
    public async Task MindMap_Timeline_Outline_ParseLlmOutput()
    {
        var chunks = Chunks("d.txt", "conteúdo do documento");
        IReadOnlyList<SearchResultItem> Getter(string p) => chunks;

        var mm = new MindMapService(Getter, new FakeLlmProvider(_ => "{\"topic\":\"Raiz\",\"children\":[{\"topic\":\"Sub\",\"children\":[]}]}"));
        var root = await mm.GenerateAsync("d.txt");
        Assert.Equal("Raiz", root.Topic);
        Assert.Single(root.Children);

        var tl = new TimelineExtractionService(Getter, new FakeLlmProvider(_ => "[{\"date\":\"2020-05-01\",\"description\":\"B\"},{\"date\":\"2019-01-01\",\"description\":\"A\"}]"));
        var events = await tl.ExtractAsync("d.txt");
        Assert.Equal("A", events[0].Description); // ordenado cronologicamente

        var ol = new DocumentOutlineService(Getter, new FakeLlmProvider(_ => "1. Intro\n2. Meio\n- Fim"));
        var outline = await ol.GenerateAsync("d.txt");
        Assert.Equal(new[] { "Intro", "Meio", "Fim" }, outline);
    }
}
