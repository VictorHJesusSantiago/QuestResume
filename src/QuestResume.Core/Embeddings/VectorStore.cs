using Microsoft.Data.Sqlite;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Embeddings;

public sealed class VectorStore : IVectorStore
{
        public const string DatabaseFileName = "vectors.db";

        private readonly object _lock = new();
    private readonly SqliteConnection _connection;
    private readonly int _maxCacheSize;
    private readonly bool _quantize;
    private readonly bool _annEnabled;
    private HnswIndex? _annIndex;
    private List<(SearchResultItem Item, float[] Embedding)>? _annEntries;
    private List<(SearchResultItem Item, float[] Embedding)>? _cache;
    private SqliteTransaction? _activeTransaction;

        public VectorStore(string indexPath, int maxCacheSize = 0, bool quantize = false, bool annEnabled = false)
    {
        _maxCacheSize = maxCacheSize;
        _quantize = quantize;
        _annEnabled = annEnabled;
        IODirectory.CreateDirectory(indexPath);
        var dbPath = Path.Combine(indexPath, DatabaseFileName);

        _connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _connection.Open();

        using (var pragma = _connection.CreateCommand())
        {
            
            
            
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

            CREATE TABLE IF NOT EXISTS image_embeddings (
                path TEXT PRIMARY KEY,
                fileName TEXT NOT NULL,
                embedding BLOB NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public void AddImageEmbedding(string sourcePath, string fileName, float[] embedding)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _activeTransaction;
            command.CommandText = """
                INSERT INTO image_embeddings (path, fileName, embedding)
                VALUES ($path, $fileName, $embedding)
                ON CONFLICT(path) DO UPDATE SET fileName = $fileName, embedding = $embedding
                """;
            command.Parameters.AddWithValue("$path", sourcePath);
            command.Parameters.AddWithValue("$fileName", fileName);
            command.Parameters.AddWithValue("$embedding", ToBytes(embedding));
            command.ExecuteNonQuery();
        }
    }

    public void RemoveImageEmbedding(string sourcePath)
    {
        lock (_lock)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _activeTransaction;
            command.CommandText = "DELETE FROM image_embeddings WHERE path = $path";
            command.Parameters.AddWithValue("$path", sourcePath);
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ImageSearchResultItem> SearchImages(float[] queryEmbedding, int topK)
    {
        lock (_lock)
        {
            var results = new List<ImageSearchResultItem>();

            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT path, fileName, embedding FROM image_embeddings";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var embedding = FromBytes((byte[])reader["embedding"]);
                results.Add(new ImageSearchResultItem
                {
                    SourcePath = reader.GetString(0),
                    FileName = reader.GetString(1),
                    Score = CosineSimilarity(queryEmbedding, embedding)
                });
            }

            return results.OrderByDescending(r => r.Score).Take(topK).ToList();
        }
    }

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

        public void InvalidateCache()
    {
        lock (_lock)
        {
            _cache = null;
        }
    }

        public IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK)
    {
        lock (_lock)
        {
            var entries = LoadCache();

            
            if (_annEnabled && entries.Count > 0)
            {
                var index = GetOrBuildAnnIndex(entries);
                var ids = index.Search(queryEmbedding, topK);
                var annResults = new List<SearchResultItem>(ids.Count);
                foreach (var id in ids)
                {
                    var (item, embedding) = entries[id];
                    annResults.Add(new SearchResultItem
                    {
                        SourcePath = item.SourcePath,
                        FileName = item.FileName,
                        ChunkIndex = item.ChunkIndex,
                        ChunkText = item.ChunkText,
                        Score = CosineSimilarity(queryEmbedding, embedding)
                    });
                }

                return annResults.OrderByDescending(r => r.Score).ToList();
            }

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

    public IReadOnlyList<(SearchResultItem Item, float[] Embedding)> GetAllEntries()
    {
        lock (_lock)
        {
            return LoadCache();
        }
    }

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

        
        
        
        if (_maxCacheSize == 0 || entries.Count <= _maxCacheSize)
        {
            _cache = entries;
        }

        return entries;
    }

        private HnswIndex GetOrBuildAnnIndex(List<(SearchResultItem Item, float[] Embedding)> entries)
    {
        if (_annIndex is not null && ReferenceEquals(_annEntries, entries))
        {
            return _annIndex;
        }

        var index = new HnswIndex(seed: 42);
        for (var i = 0; i < entries.Count; i++)
        {
            index.Add(i, entries[i].Embedding);
        }

        _annIndex = index;
        _annEntries = entries;
        return index;
    }

    private byte[] ToBytes(float[] embedding)
    {
        if (_quantize)
        {
            return VectorQuantizer.ToQuantizedBytes(embedding);
        }

        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        if (VectorQuantizer.IsQuantized(bytes))
        {
            return VectorQuantizer.FromQuantizedBytes(bytes);
        }

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
