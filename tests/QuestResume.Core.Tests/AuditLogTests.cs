using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class AuditLogTests
{
    [Fact]
    public void Append_ThenLoad_ReturnsEntriesMostRecentFirst()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"audit-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            AuditLog.Append(indexPath, new AuditLogEntry
            {
                TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Question = "Primeira pergunta?",
                Sources = new List<string> { "a.txt" }
            });

            AuditLog.Append(indexPath, new AuditLogEntry
            {
                TimestampUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                Question = "Segunda pergunta?",
                Sources = new List<string> { "b.txt" }
            });

            var entries = AuditLog.Load(indexPath);

            Assert.Equal(2, entries.Count);
            Assert.Equal("Segunda pergunta?", entries[0].Question);
            Assert.Equal("Primeira pergunta?", entries[1].Question);
            Assert.Equal(new[] { "b.txt" }, entries[0].Sources);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyList()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"audit-missing-{Guid.NewGuid()}");

        var entries = AuditLog.Load(indexPath);

        Assert.Empty(entries);
    }

    [Fact]
    public void Load_WithLimit_ReturnsOnlyMostRecentN()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"audit-limit-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            for (var i = 1; i <= 5; i++)
            {
                AuditLog.Append(indexPath, new AuditLogEntry
                {
                    TimestampUtc = new DateTime(2026, 1, i, 0, 0, 0, DateTimeKind.Utc),
                    Question = $"Pergunta {i}"
                });
            }

            var entries = AuditLog.Load(indexPath, limit: 2);

            Assert.Equal(2, entries.Count);
            Assert.Equal("Pergunta 5", entries[0].Question);
            Assert.Equal("Pergunta 4", entries[1].Question);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
