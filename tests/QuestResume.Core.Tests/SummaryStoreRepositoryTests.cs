using QuestResume.Core.Models;
using QuestResume.Core.Persistence;

namespace QuestResume.Core.Tests;

public class SummaryStoreRepositoryTests
{
    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"missing-summarystore-{Guid.NewGuid()}");
        var repository = new SummaryStoreRepository(indexPath);

        var store = repository.Load();

        Assert.Empty(store.Summaries);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSummaries()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"summarystore-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            var repository = new SummaryStoreRepository(indexPath);
            var store = new SummaryStore();
            store.SetSummary("doc.txt", "Resumo do documento.");
            repository.Save(store);

            var loaded = repository.Load();

            Assert.Equal("Resumo do documento.", loaded.GetSummary("doc.txt"));
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void GetSummary_UnknownPath_ReturnsNull()
    {
        var store = new SummaryStore();

        Assert.Null(store.GetSummary("nao-existe.txt"));
    }

    [Fact]
    public void SetSummary_EmptyValue_RemovesEntry()
    {
        var store = new SummaryStore();
        store.SetSummary("doc.txt", "algo");

        store.SetSummary("doc.txt", "");

        Assert.Null(store.GetSummary("doc.txt"));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmptyStore()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"corrupt-summarystore-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            File.WriteAllText(Path.Combine(indexPath, SummaryStore.FileName), "{ not valid json");
            var repository = new SummaryStoreRepository(indexPath);

            var store = repository.Load();

            Assert.Empty(store.Summaries);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
