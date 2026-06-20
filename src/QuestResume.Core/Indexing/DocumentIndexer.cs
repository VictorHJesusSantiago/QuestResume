using System.Security.Cryptography;
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
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVectorStore? _vectorStore;

    public DocumentIndexer(ExtractorRegistry? registry = null, IEmbeddingService? embeddingService = null, IVectorStore? vectorStore = null)
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
        CancellationToken cancellationToken = default,
        long maxFileSizeBytes = 0,
        IReadOnlyList<string>? excludedFolders = null,
        bool piiRedactionEnabled = false)
    {
        if (!IODirectory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folderPath}");
        }

        IODirectory.CreateDirectory(indexPath);

        var stats = new IndexStats();

        // If a previous SwapIndexDirectory was interrupted mid-delete (crash, power loss),
        // a _swap_pending.marker file is left behind. The index may be in a partial state,
        // but since we're about to rebuild it entirely we just remove the marker and continue.
        var swapMarkerPath = Path.Combine(indexPath, "_swap_pending.marker");
        if (File.Exists(swapMarkerPath))
        {
            File.Delete(swapMarkerPath);
        }

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

                // Maps content hash -> path of the first file with that content seen in this
                // run, so byte-identical duplicates are detected and indexed only once.
                var seenHashes = new Dictionary<string, string>();

                // Materialized upfront (instead of streamed via EnumerateFiles) so the total
                // file count is known, letting progress messages show "[i/total]" — a coarse
                // but real progress indicator for the CLI/Desktop status line.
                var allFiles = IODirectory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).ToList();
                var fileIndex = 0;

                foreach (var filePath in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fileIndex++;
                    var prefix = $"[{fileIndex}/{allFiles.Count}]";

                    if (excludedFolders is not null && IsUnderExcludedFolder(filePath, excludedFolders))
                    {
                        stats.FilesSkipped++;
                        stats.SkippedFiles.Add(filePath);
                        continue;
                    }

                    var extension = Path.GetExtension(filePath);
                    if (!_registry.IsSupported(extension))
                    {
                        stats.FilesSkipped++;
                        stats.SkippedFiles.Add(filePath);
                        continue;
                    }

                    if (maxFileSizeBytes > 0 && new FileInfo(filePath).Length > maxFileSizeBytes)
                    {
                        stats.FilesSkipped++;
                        stats.SkippedFiles.Add(filePath);
                        progress?.Report($"{prefix} Arquivo excede o tamanho máximo configurado, ignorado: {filePath}");
                        continue;
                    }

                    try
                    {
                        var hash = await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
                        if (seenHashes.TryGetValue(hash, out var originalPath))
                        {
                            stats.Duplicates.Add(new DuplicateFile { Path = filePath, DuplicateOfPath = originalPath });
                            progress?.Report($"{prefix} Duplicado (idêntico a {Path.GetFileName(originalPath)}): {filePath}");
                            continue;
                        }

                        seenHashes[hash] = filePath;

                        var document = await _registry.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
                        var chunks = TextChunker.CodeExtensions.Contains(extension)
                            ? TextChunker.ChunkCode(document, chunkSize, overlap)
                            : TextChunker.Chunk(document, chunkSize, overlap);

                        foreach (var chunk in chunks)
                        {
                            var content = piiRedactionEnabled ? PiiRedactor.Redact(chunk.Text) : chunk.Text;
                            writer.AddDocument(ToLuceneDocument(chunk, content));

                            if (embeddingsAvailable)
                            {
                                try
                                {
                                    var embedding = await _embeddingService!.EmbedAsync(content, cancellationToken).ConfigureAwait(false);
                                    _vectorStore!.Add(chunk.SourcePath, chunk.FileName, chunk.ChunkIndex, content, embedding);
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
                        progress?.Report($"{prefix} Indexado: {document.FileName} ({chunks.Count} trecho(s))");
                    }
                    catch (Exception ex)
                    {
                        stats.Errors.Add($"{filePath}: {ex.Message}");
                        progress?.Report($"{prefix} Erro ao processar {filePath}: {ex.Message}");
                    }
                }

                writer.Commit();

                // Re-indexing rebuilds the whole index from scratch (CREATE mode), which can
                // leave many small segments. Merge down to a single segment for faster searches
                // and a smaller on-disk footprint, then commit the merge.
                writer.ForceMerge(1);
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

        new IndexReport
        {
            GeneratedUtc = DateTime.UtcNow,
            Errors = stats.Errors,
            Duplicates = stats.Duplicates
        }.Save(indexPath);

        return stats;
    }

    /// <summary>
    /// Returns true if <paramref name="filePath"/> is inside any folder listed in
    /// <paramref name="excludedFolders"/> (or equal to one of them), so it's never indexed even
    /// if it's under <c>folderPath</c>.
    /// </summary>
    private static bool IsUnderExcludedFolder(string filePath, IReadOnlyList<string> excludedFolders)
    {
        var fullFilePath = Path.GetFullPath(filePath);

        foreach (var folder in excludedFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            var fullFolderPath = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullFilePath.Equals(fullFolderPath, StringComparison.OrdinalIgnoreCase)
                || fullFilePath.StartsWith(fullFolderPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Computes the SHA-256 hash of a file's contents, used to detect duplicate files.</summary>
    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private const string SwapMarkerFileName = "_swap_pending.marker";

    /// <summary>
    /// Replaces the Lucene index files in <paramref name="indexPath"/> with the freshly built
    /// ones from <paramref name="tempIndexDir"/>, leaving <see cref="VectorStore.DatabaseFileName"/>,
    /// <see cref="TagStore.FileName"/>, and <see cref="AuditLog.FileName"/> untouched.
    ///
    /// Writes a <c>_swap_pending.marker</c> file before deleting anything and removes it after
    /// the move completes. If the process is killed between those two points, the next call to
    /// <see cref="IndexFolderAsync"/> detects the marker and removes it, then proceeds with a
    /// clean rebuild — safe because a full re-index is about to overwrite the index anyway.
    /// </summary>
    private static void SwapIndexDirectory(string indexPath, string tempIndexDir)
    {
        var markerPath = Path.Combine(indexPath, SwapMarkerFileName);
        File.WriteAllText(markerPath, string.Empty);

        foreach (var file in IODirectory.GetFiles(indexPath))
        {
            var fileName = Path.GetFileName(file);

            // Skip vectors.db and its WAL/SHM/journal side-files (e.g. "vectors.db-wal"),
            // which remain open via VectorStore's connection.
            if (fileName.StartsWith(VectorStore.DatabaseFileName, StringComparison.Ordinal))
            {
                continue;
            }

            // User-assigned tags are keyed by source file path, not by index content, so they
            // must survive a re-index.
            if (fileName == TagStore.FileName)
            {
                continue;
            }

            // Audit log records question history across re-indexes; deleting it on every
            // re-index silently loses that history (audit item A3).
            if (fileName == AuditLog.FileName)
            {
                continue;
            }

            // The marker itself must not be deleted mid-swap.
            if (fileName == SwapMarkerFileName)
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

        File.Delete(markerPath);
    }

    private static Document ToLuceneDocument(TextChunk chunk, string content) => new()
    {
        new StringField("path", chunk.SourcePath, Field.Store.YES),
        new StringField("fileName", chunk.FileName, Field.Store.YES),
        new StringField("extension", Path.GetExtension(chunk.FileName).ToLowerInvariant(), Field.Store.YES),
        new StoredField("chunkIndex", chunk.ChunkIndex.ToString()),
        new TextField("content", content, Field.Store.YES),
        new StringField("modifiedUtc", chunk.ModifiedUtc.Ticks.ToString(), Field.Store.YES)
    };
}
