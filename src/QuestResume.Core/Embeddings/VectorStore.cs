using Microsoft.Data.Sqlite;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Embeddings;

/// <summary>
/// Persists chunk embeddings in a SQLite database (one file alongside the Lucene index) and
/// performs cosine-similarity search over them in memory. There is no .NET binding for
/// sqlite-vec, so this is a simple, dependency-free vector store suited to the scale of a
/// single user's document collection (thousands of chunks).
/// </summary>
public sealed class VectorStore : IDisposable
{
    private readonly SqliteConnection _connection;

    public VectorStore(string indexPath)
    {
        IODirectory.CreateDirectory(indexPath);
        var dbPath = Path.Combine(indexPath, "vectors.db");

        _connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _connection.Open();

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                fileName TEXT NOT NULL,
                chunkIndex INTEGER NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL
            )
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Removes every stored embedding, so re-indexing starts from a clean slate.</summary>
    public void Clear()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chunks";
        command.ExecuteNonQuery();
    }

    public void Add(string sourcePath, string fileName, int chunkIndex, string text, float[] embedding)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chunks (path, fileName, chunkIndex, text, embedding)
            VALUES ($path, $fileName, $chunkIndex, $text, $embedding)
            """;
        command.Parameters.AddWithValue("$path", sourcePath);
        command.Parameters.AddWithValue("$fileName", fileName);
        command.Parameters.AddWithValue("$chunkIndex", chunkIndex);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$embedding", ToBytes(embedding));
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Returns the <paramref name="topK"/> stored chunks whose embeddings are most similar
    /// (cosine similarity) to <paramref name="queryEmbedding"/>.
    /// </summary>
    public IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK)
    {
        var scored = new List<SearchResultItem>();

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT path, fileName, chunkIndex, text, embedding FROM chunks";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var embedding = FromBytes((byte[])reader["embedding"]);
            var score = CosineSimilarity(queryEmbedding, embedding);

            scored.Add(new SearchResultItem
            {
                SourcePath = reader.GetString(0),
                FileName = reader.GetString(1),
                ChunkIndex = reader.GetInt32(2),
                ChunkText = reader.GetString(3),
                Score = score
            });
        }

        return scored
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .ToList();
    }

    private static byte[] ToBytes(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        var embedding = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, embedding, 0, bytes.Length);
        return embedding;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f)
        {
            return 0f;
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    public void Dispose() => _connection.Dispose();
}
