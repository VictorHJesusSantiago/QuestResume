using QuestResume.Core.Indexing;
using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class DashboardStatsTests
{
    [Fact]
    public async Task Compute_ReflectsIndexedFilesAndAudit()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"stats-docs-{Guid.NewGuid()}");
        var indexPath = Path.Combine(Path.GetTempPath(), $"stats-index-{Guid.NewGuid()}");

        Directory.CreateDirectory(folder);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "a.txt"), "Conteúdo do arquivo A.");
            await File.WriteAllTextAsync(Path.Combine(folder, "b.txt"), "Conteúdo do arquivo B.");

            var indexer = new DocumentIndexer();
            await indexer.IndexFolderAsync(folder, indexPath);

            AuditLog.Append(indexPath, new AuditLogEntry { TimestampUtc = DateTime.UtcNow, Question = "Pergunta de teste?", ElapsedMs = 200 });

            var stats = DashboardStats.Compute(indexPath);

            Assert.Equal(2, stats.DocumentCount);
            Assert.Equal(2, stats.ChunkCount);
            Assert.Equal(1, stats.QuestionCount);
            Assert.Equal(0, stats.ErrorCount);
            Assert.NotNull(stats.LastIndexedUtc);
            Assert.True(stats.IndexSizeBytes > 0);
            Assert.True(stats.ProcessMemoryMb > 0);
            Assert.Equal(200, stats.AverageResponseTimeMs);
            Assert.False(stats.ModelConfigured);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void Compute_MissingIndex_ReturnsZeros()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"stats-missing-{Guid.NewGuid()}");

        var stats = DashboardStats.Compute(indexPath);

        Assert.Equal(0, stats.DocumentCount);
        Assert.Equal(0, stats.ChunkCount);
        Assert.Equal(0, stats.QuestionCount);
        Assert.Null(stats.LastIndexedUtc);
        Assert.Null(stats.AverageResponseTimeMs);
        Assert.False(stats.ModelConfigured);
    }
}
