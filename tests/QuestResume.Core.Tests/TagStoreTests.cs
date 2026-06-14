using QuestResume.Core.Models;

namespace QuestResume.Core.Tests;

public class TagStoreTests
{
    [Fact]
    public void SetTags_NormalizesTrimsAndDeduplicates()
    {
        var store = new TagStore();

        store.SetTags("doc.txt", new[] { " Contrato ", "contrato", "Urgente", "" });

        Assert.Equal(new[] { "Contrato", "Urgente" }, store.GetTags("doc.txt"));
    }

    [Fact]
    public void SetTags_EmptyList_RemovesEntry()
    {
        var store = new TagStore();
        store.SetTags("doc.txt", new[] { "Contrato" });

        store.SetTags("doc.txt", Array.Empty<string>());

        Assert.Empty(store.GetTags("doc.txt"));
        Assert.False(store.DocumentTags.ContainsKey("doc.txt"));
    }

    [Fact]
    public void GetTags_UnknownPath_ReturnsEmpty()
    {
        var store = new TagStore();

        Assert.Empty(store.GetTags("nao-existe.txt"));
    }

    [Fact]
    public void GetAllTags_ReturnsDistinctSortedAcrossDocuments()
    {
        var store = new TagStore();
        store.SetTags("a.txt", new[] { "Beta", "Alpha" });
        store.SetTags("b.txt", new[] { "alpha", "Gamma" });

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, store.GetAllTags());
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTags()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"tagstore-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            var store = new TagStore();
            store.SetTags("doc.txt", new[] { "Contrato", "2026" });
            store.Save(indexPath);

            var loaded = TagStore.Load(indexPath);

            Assert.Equal(new[] { "Contrato", "2026" }, loaded.GetTags("doc.txt"));
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyStore()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"missing-tagstore-{Guid.NewGuid()}");

        var store = TagStore.Load(indexPath);

        Assert.Empty(store.DocumentTags);
    }
}
