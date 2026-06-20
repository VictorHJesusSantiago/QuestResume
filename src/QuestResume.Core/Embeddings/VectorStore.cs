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
public sealed class VectorStore : IVectorStore
{
    /// <summary>File name of the SQLite database, relative to the index folder.</summary>
    public const string DatabaseFileName = "vectors.db";

    /// <summary>
    /// Guards <see cref="_connection"/> and <see cref="_cache"/>. Microsoft.Data.Sqlite does not
    /// guarantee a single <see cref="SqliteConnection"/> is safe for concurrent use from
    /// multiple threads, and this instance may be shared (e.g. via a singleton RAG engine)
    /// across concurrent API requests.
    /// </summary>
    private readonly object _lock = new();
    private readonly SqliteConnection _connection;
    private readonly int _maxCacheSize;
    private List<(SearchResultItem Item, float[] Embedding)>? _cache;
    private SqliteTransaction? _activeTransaction;

    /// <param name="maxCacheSize">
    /// Maximum number of embeddings kept in the in-memory cache. 0 = no limit (load all).
    /// When the stored count exceeds this value the cache is skipped and every
    /// <see cref="Search"/> call re-reads from SQLite — prevents OOM for very large collections.
    /// </param>
    public VectorStore(string indexPath, int maxCacheSize = 0)
    {
        _maxCacheSize = maxCacheSize;
        IODirectory.CreateDirectory(indexPath);
        var dbPath = Path.Combine(indexPath, DatabaseFileName);

        _connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            // WAL + busy_timeout let a long-lived reader (RagEngineProvider's cached engine)
            // and a short-lived writer (a re-index request) touch vectors.db at the same time
            // without an immediate "database is locked" SqliteException.
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                fileName TEXT NOT NULL,
                chunkIndex INTEGER NOT NULL,
                text TEXT NOT NULL,
                embedding BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_chunks_path ON chunks(path);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Removes every stored embedding, so re-indexing starts from a clean slate.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _activeTransaction;
            command.CommandText = "DELETE FROM chunks";
            command.ExecuteNonQuery();
            _cache = null;
        }
    }

    /// <summary>
    /// Wraps every <see cref="Add"/> call made until the returned handle is disposed in a
    /// single SQLite transaction, so bulk-inserting thousands of chunks during indexing pays
    /// one fsync instead of one per chunk. Dispose the handle to commit.
    /// </summary>
    public IDisposable BeginBatch()
    {
        lock (_lock)
        {
            _activeTransaction ??= _connection.BeginTransaction();
            return new BatchScope(this);
        }
    }

    private void EndBatch()
    {
        lock (_lock)
        {
            using var transaction = _activeTransaction;
            _activeTransaction = null;
            transaction?.Commit();
        }
    }

    private sealed class BatchScope(VectorStore store) : IDisposable
    {
        public void Dispose() => store.EndBatch();
    }

    public void Add(string sourcePath, string fileName, int chunkIndex, string text, float[] embedding)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _activeTransaction;
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
            _cache = null;
        }
    }

    /// <summary>
    /// Removes every stored embedding for the given source file, used when a single document is
    /// removed from the index without a full re-index.
    /// </summary>
    public void RemoveBySourcePath(string sourcePath)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _activeTransaction;
            command.CommandText = "DELETE FROM chunks WHERE path = $path";
            command.Parameters.AddWithValue("$path", sourcePath);
            command.ExecuteNonQuery();
            _cache = null;
        }
    }

    /// <summary>
    /// Drops the in-memory cache without touching the underlying table. Call this after a
    /// different <see cref="VectorStore"/> instance pointed at the same <c>vectors.db</c> has
    /// re-indexed, so this instance doesn't keep serving a stale pre-reindex snapshot.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }

    /// <summary>
    /// Returns the <paramref name="topK"/> stored chunks whose embeddings are most similar
    /// (cosine similarity) to <paramref name="queryEmbedding"/>.
    /// </summary>
    public IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK)
    {
        lock (_lock)
        {
            var entries = LoadCache();
            var scored = new List<SearchResultItem>(entries.Count);

            foreach (var (item, embedding) in entries)
            {
                scored.Add(new SearchResultItem
                {
                    SourcePath = item.SourcePath,
                    FileName = item.FileName,
                    ChunkIndex = item.ChunkIndex,
                    ChunkText = item.ChunkText,
                    Score = CosineSimilarity(queryEmbedding, embedding)
                });
            }

            return scored
                .OrderByDescending(item => item.Score)
                .Take(topK)
                .ToList();
        }
    }

    /// <summary>
    /// Loads every stored chunk and its embedding into memory on first use, reusing the cache on
    /// subsequent searches until <see cref="Add"/>, <see cref="Clear"/> or
    /// <see cref="InvalidateCache"/> invalidates it. Keeps repeated queries from re-reading and
    /// re-deserializing the whole table from SQLite. Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private List<(SearchResultItem Item, float[] Embedding)> LoadCache()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var entries = new List<(SearchResultItem Item, float[] Embedding)>();

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT path, fileName, chunkIndex, text, embedding FROM chunks";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var item = new SearchResultItem
            {
                SourcePath = reader.GetString(0),
                FileName = reader.GetString(1),
                ChunkIndex = reader.GetInt32(2),
                ChunkText = reader.GetString(3),
                Score = 0
            };
            entries.Add((item, FromBytes((byte[])reader["embedding"])));
        }

        // Only cache when within the configured size limit. When the collection exceeds
        // MaxCacheSize the cache is intentionally left null so every search re-reads from
        // SQLite instead of exhausting available memory with gigabytes of float arrays.
        if (_maxCacheSize == 0 || entries.Count <= _maxCacheSize)
        {
            _cache = entries;
        }

        return entries;
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

    public void Dispose()
    {
        lock (_lock)
        {
            _activeTransaction?.Dispose();
            _connection.Dispose();
        }
    }
}
