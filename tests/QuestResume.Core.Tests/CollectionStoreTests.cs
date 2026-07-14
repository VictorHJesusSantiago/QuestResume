using QuestResume.Core.Persistence;

namespace QuestResume.Core.Tests;

public class CollectionStoreTests
{
    private static string NewBasePath() => Path.Combine(Path.GetTempPath(), $"collectionstore-{Guid.NewGuid()}");

    [Fact]
    public void List_AlwaysIncludesDefault()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        var collections = store.List();

        Assert.Contains(collections, c => c.Nome == CollectionStore.DefaultName && c.Caminho == basePath);
    }

    [Fact]
    public void Create_AddsCollectionWithPhysicalPathUnderCollectionsFolder()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        try
        {
            var created = store.Create("trabalho");

            Assert.Equal("trabalho", created.Nome);
            Assert.Equal(Path.Combine(basePath, "collections", "trabalho"), created.Caminho);
            Assert.True(Directory.Exists(created.Caminho));
            Assert.Contains(store.List(), c => c.Nome == "trabalho");
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public void Create_DuplicateName_Throws()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        try
        {
            store.Create("pessoal");
            Assert.Throws<InvalidOperationException>(() => store.Create("pessoal"));
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public void Create_DefaultName_Throws()
    {
        var store = new CollectionStore(NewBasePath());

        Assert.Throws<InvalidOperationException>(() => store.Create("default"));
    }

    [Fact]
    public void Delete_RemovesFromCatalog()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        try
        {
            store.Create("temporaria");
            var removed = store.Delete("temporaria");

            Assert.True(removed);
            Assert.DoesNotContain(store.List(), c => c.Nome == "temporaria");
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }

    [Fact]
    public void Delete_Default_Throws()
    {
        var store = new CollectionStore(NewBasePath());

        Assert.Throws<InvalidOperationException>(() => store.Delete("default"));
    }

    [Fact]
    public void Delete_UnknownCollection_ReturnsFalse()
    {
        var store = new CollectionStore(NewBasePath());

        Assert.False(store.Delete("nao-existe"));
    }

    [Fact]
    public void ResolvePath_Default_ReturnsBasePath()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        Assert.Equal(basePath, store.ResolvePath("default"));
        Assert.Equal(basePath, store.ResolvePath(""));
    }

    [Fact]
    public void ResolvePath_UnknownCollection_AutoCreates()
    {
        var basePath = NewBasePath();
        var store = new CollectionStore(basePath);

        try
        {
            var path = store.ResolvePath("nova");

            Assert.Equal(Path.Combine(basePath, "collections", "nova"), path);
            Assert.Contains(store.List(), c => c.Nome == "nova");
        }
        finally
        {
            if (Directory.Exists(basePath)) Directory.Delete(basePath, recursive: true);
        }
    }
}
