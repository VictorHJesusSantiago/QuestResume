using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Walks a folder, extracts text from every supported file and writes the resulting
/// chunks to a Lucene.NET full-text index on disk.
/// </summary>
public sealed class DocumentIndexer
{
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    private readonly ExtractorRegistry _registry;
    private readonly EmbeddingService? _embeddingService;
    private readonly VectorStore? _vectorStore;

    public DocumentIndexer(ExtractorRegistry? registry = null, EmbeddingService? embeddingService = null, VectorStore? vectorStore = null)
    {
        _registry = registry ?? new ExtractorRegistry();
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
    }

    /// <summary>
    /// Indexes every supported file under <paramref name="folderPath"/> (recursively),
    /// replacing any existing index at <paramref name="indexPath"/>.
    /// </summary>
    public async Task<IndexStats> IndexFolderAsync(
        string folderPath,
        string indexPath,
        int chunkSize = 1000,
        int overlap = 150,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IODirectory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folderPath}");
        }

        IODirectory.CreateDirectory(indexPath);

        var stats = new IndexStats();

        // Built in a temporary sibling directory and only swapped into place once the new
        // Lucene index has been fully committed. With the previous OpenMode.CREATE-on-indexPath
        // approach, a crash or cancellation midway through a (potentially long) re-index left
        // the search index empty/corrupted for the rest of the session.
        var tempIndexDir = Path.Combine(indexPath, "_index_tmp");
        if (IODirectory.Exists(tempIndexDir))
        {
            IODirectory.Delete(tempIndexDir, recursive: true);
        }

        IODirectory.CreateDirectory(tempIndexDir);

        try
        {
            using (var directory = FSDirectory.Open(tempIndexDir))
            using (var analyzer = new StandardAnalyzer(MatchVersion))
            {
                var config = new IndexWriterConfig(MatchVersion, analyzer)
                {
                    OpenMode = OpenMode.CREATE
                };

                using var writer = new IndexWriter(directory, config);

                _vectorStore?.Clear();
                var embeddingsAvailable = _embeddingService is not null && _vectorStore is not null;

                // Wrap all vector inserts in a single SQLite transaction instead of one
                // commit per chunk, which dominates indexing time for large folders.
                using var batch = _vectorStore?.BeginBatch();

                foreach (var filePath in IODirectory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var extension = Path.GetExtension(filePath);
                    if (!_registry.IsSupported(extension))
                    {
                        stats.FilesSkipped++;
                        stats.SkippedFiles.Add(filePath);
                        continue;
                    }

                    try
                    {
                        var document = await _registry.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
                        var chunks = TextChunker.Chunk(document, chunkSize, overlap);

                        foreach (var chunk in chunks)
                        {
                            writer.AddDocument(ToLuceneDocument(chunk));

                            if (embeddingsAvailable)
                            {
                                try
                                {
                                    var embedding = await _embeddingService!.EmbedAsync(chunk.Text, cancellationToken).ConfigureAwait(false);
                                    _vectorStore!.Add(chunk.SourcePath, chunk.FileName, chunk.ChunkIndex, chunk.Text, embedding);
                                }
                                catch (EmbeddingsNotConfiguredException ex)
                                {
                                    embeddingsAvailable = false;
                                    progress?.Report($"Embeddings desabilitados durante a indexação: {ex.Message}");
                                }
                            }
                        }

                        stats.FilesProcessed++;
                        stats.ChunksIndexed += chunks.Count;
                        progress?.Report($"Indexado: {document.FileName} ({chunks.Count} trecho(s))");
                    }
                    catch (Exception ex)
                    {
                        stats.Errors.Add($"{filePath}: {ex.Message}");
                        progress?.Report($"Erro ao processar {filePath}: {ex.Message}");
                    }
                }

                writer.Commit();
            }

            SwapIndexDirectory(indexPath, tempIndexDir);
        }
        finally
        {
            if (IODirectory.Exists(tempIndexDir))
            {
                IODirectory.Delete(tempIndexDir, recursive: true);
            }
        }

        return stats;
    }

    /// <summary>
    /// Replaces the Lucene index files in <paramref name="indexPath"/> with the freshly built
    /// ones from <paramref name="tempIndexDir"/>, leaving <see cref="VectorStore.DatabaseFileName"/>
    /// untouched (it's managed separately by <see cref="VectorStore"/> and may have an open
    /// connection/lock on it).
    /// </summary>
    private static void SwapIndexDirectory(string indexPath, string tempIndexDir)
    {
        foreach (var file in IODirectory.GetFiles(indexPath))
        {
            // Skip vectors.db and its WAL/SHM/journal side-files (e.g. "vectors.db-wal"),
            // which remain open via VectorStore's connection.
            if (Path.GetFileName(file).StartsWith(VectorStore.DatabaseFileName, StringComparison.Ordinal))
            {
                continue;
            }

            File.Delete(file);
        }

        foreach (var file in IODirectory.GetFiles(tempIndexDir))
        {
            var destination = Path.Combine(indexPath, Path.GetFileName(file));
            File.Move(file, destination, overwrite: true);
        }
    }

    private static Document ToLuceneDocument(TextChunk chunk) => new()
    {
        new StringField("path", chunk.SourcePath, Field.Store.YES),
        new StringField("fileName", chunk.FileName, Field.Store.YES),
        new StoredField("chunkIndex", chunk.ChunkIndex.ToString()),
        new TextField("content", chunk.Text, Field.Store.YES),
        new StringField("modifiedUtc", chunk.ModifiedUtc.Ticks.ToString(), Field.Store.YES)
    };
}
