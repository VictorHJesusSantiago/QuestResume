using QuestResume.Core.Embeddings;
using Xunit;

namespace QuestResume.Core.Tests;

public class HnswIndexTests
{
    private static float[] Rand(Random r, int dim)
    {
        var v = new float[dim];
        for (var i = 0; i < dim; i++)
        {
            v[i] = (float)(r.NextDouble() * 2 - 1);
        }
        return v;
    }

    private static double Cosine(float[] a, float[] b)
    {
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        var index = new HnswIndex(seed: 1);
        Assert.Empty(index.Search(new[] { 1f, 0f }, 5));
    }

    [Fact]
    public void Search_FindsExactMatch()
    {
        var index = new HnswIndex(seed: 1);
        var vectors = new List<float[]>
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
            new[] { 0f, 0f, 1f },
        };
        for (var i = 0; i < vectors.Count; i++)
        {
            index.Add(i, vectors[i]);
        }

        var result = index.Search(new[] { 0.9f, 0.1f, 0f }, 1);
        Assert.Single(result);
        Assert.Equal(0, result[0]);
    }

    [Fact]
    public void Search_HighRecall_VsBruteForce()
    {
        const int dim = 32;
        const int n = 500;
        var r = new Random(123);
        var data = new List<float[]>();
        var index = new HnswIndex(m: 16, efConstruction: 200, seed: 7);
        for (var i = 0; i < n; i++)
        {
            var v = Rand(r, dim);
            data.Add(v);
            index.Add(i, v);
        }

        var hits = 0;
        const int queries = 40;
        const int k = 10;
        for (var q = 0; q < queries; q++)
        {
            var query = Rand(r, dim);
            var bruteTop = Enumerable.Range(0, n)
                .OrderByDescending(i => Cosine(query, data[i]))
                .Take(k)
                .ToHashSet();
            var ann = index.Search(query, k, ef: 64);
            hits += ann.Count(bruteTop.Contains);
        }

        // Recall médio: esperamos alto (>0.85) para HNSW bem configurado neste tamanho.
        var recall = hits / (double)(queries * k);
        Assert.True(recall > 0.85, $"Recall {recall:P0} abaixo do esperado.");
    }
}

public class VectorStoreAnnQuantizationTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"qr-vs-{Guid.NewGuid():N}");

    [Fact]
    public void Quantized_RoundTrip_PreservesApproxSearch()
    {
        var dir = TempDir();
        try
        {
            using var store = new VectorStore(dir, quantize: true);
            store.Add("a.txt", "a.txt", 0, "alpha", new[] { 1f, 0f, 0f });
            store.Add("b.txt", "b.txt", 0, "beta", new[] { 0f, 1f, 0f });

            var results = store.Search(new[] { 0.95f, 0.05f, 0f }, 1);
            Assert.Single(results);
            Assert.Equal("a.txt", results[0].SourcePath);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void Ann_ReturnsTopResult()
    {
        var dir = TempDir();
        try
        {
            using var store = new VectorStore(dir, annEnabled: true);
            for (var i = 0; i < 50; i++)
            {
                var v = new float[8];
                v[i % 8] = 1f;
                store.Add($"f{i}.txt", $"f{i}.txt", 0, $"chunk{i}", v);
            }

            var query = new float[8];
            query[3] = 1f;
            var results = store.Search(query, 3);
            Assert.NotEmpty(results);
            // O melhor resultado deve ter alta similaridade de cosseno com a query.
            Assert.True(results[0].Score > 0.9);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
