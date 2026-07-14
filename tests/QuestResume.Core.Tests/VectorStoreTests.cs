using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

public class VectorStoreTests
{
    [Fact]
    public void SearchImages_RanksByCosineSimilarity()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"vectorstore-img-{Guid.NewGuid()}");

        try
        {
            using var store = new VectorStore(indexPath);

            store.AddImageEmbedding("a.png", "a.png", new float[] { 1f, 0f, 0f, 0f });
            store.AddImageEmbedding("b.png", "b.png", new float[] { 0f, 1f, 0f, 0f });

            var results = store.SearchImages(new float[] { 1f, 0f, 0f, 0f }, topK: 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("a.png", results[0].FileName);
            Assert.True(results[0].Score > results[1].Score);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void RemoveImageEmbedding_RemovesOnlyMatchingEntry()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"vectorstore-img-{Guid.NewGuid()}");

        try
        {
            using var store = new VectorStore(indexPath);
            store.AddImageEmbedding("a.png", "a.png", new float[] { 1f, 0f });
            store.AddImageEmbedding("b.png", "b.png", new float[] { 0f, 1f });

            store.RemoveImageEmbedding("a.png");

            var results = store.SearchImages(new float[] { 1f, 0f }, topK: 10);
            Assert.Single(results);
            Assert.Equal("b.png", results[0].FileName);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }


    [Fact]
    public void Search_RanksResultsByCosineSimilarity()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"vectorstore-{Guid.NewGuid()}");

        try
        {
            using var store = new VectorStore(indexPath);

            store.Add("a.txt", "a.txt", 0, "chunk a", new float[] { 1f, 0f, 0f, 0f });
            store.Add("b.txt", "b.txt", 0, "chunk b", new float[] { 0f, 1f, 0f, 0f });
            store.Add("c.txt", "c.txt", 0, "chunk c", new float[] { 0.9f, 0.1f, 0f, 0f });

            var results = store.Search(new float[] { 1f, 0f, 0f, 0f }, topK: 2);

            Assert.Equal(2, results.Count);
            Assert.Equal("a.txt", results[0].FileName);
            Assert.Equal("c.txt", results[1].FileName);
            Assert.True(results[0].Score > results[1].Score);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void RemoveBySourcePath_RemovesOnlyMatchingChunks()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"vectorstore-{Guid.NewGuid()}");

        try
        {
            using var store = new VectorStore(indexPath);

            store.Add("a.txt", "a.txt", 0, "chunk a", new float[] { 1f, 0f, 0f, 0f });
            store.Add("b.txt", "b.txt", 0, "chunk b", new float[] { 0f, 1f, 0f, 0f });

            store.RemoveBySourcePath("a.txt");

            var results = store.Search(new float[] { 1f, 0f, 0f, 0f }, topK: 5);

            var remaining = Assert.Single(results);
            Assert.Equal("b.txt", remaining.FileName);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }

    [Fact]
    public void Clear_RemovesAllStoredEmbeddings()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"vectorstore-{Guid.NewGuid()}");

        try
        {
            using var store = new VectorStore(indexPath);
            store.Add("a.txt", "a.txt", 0, "chunk a", new float[] { 1f, 0f, 0f, 0f });

            store.Clear();

            var results = store.Search(new float[] { 1f, 0f, 0f, 0f }, topK: 5);

            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
