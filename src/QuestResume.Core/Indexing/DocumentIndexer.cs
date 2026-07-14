using System.Collections.Concurrent;
using System.Security.Cryptography;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using QuestResume.Core.Embeddings;
using QuestResume.Core.Extraction;
using QuestResume.Core.Models;
using QuestResume.Core.Notifications;
using QuestResume.Core.Persistence;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Walks a folder, extracts text from every supported file and writes the resulting
/// chunks to a Lucene.NET full-text index on disk.
/// </summary>
public sealed class DocumentIndexer
{
    public const LuceneVersion MatchVersion = LuceneVersion.LUCENE_48;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif" };

    private readonly ExtractorRegistry _registry;
    private readonly IEmbeddingService? _embeddingService;
    private readonly IVectorStore? _vectorStore;
    private readonly QuestResume.Core.Embeddings.IClipEmbeddingService? _clipService;

    public DocumentIndexer(
        ExtractorRegistry? registry = null,
        IEmbeddingService? embeddingService = null,
        IVectorStore? vectorStore = null,
        QuestResume.Core.Embeddings.IClipEmbeddingService? clipService = null)
    {
        _registry = registry ?? new ExtractorRegistry();
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _clipService = clipService;
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
        bool piiRedactionEnabled = false,
        int parallelism = 0,
        bool incrementalIndexingEnabled = false,
        string? masterPassword = null,
        string? masterKeyVerifier = null,
        bool autoSummarizationEnabled = false,
        Rag.ILlmProvider? llmProvider = null,
        WebhookNotifier? webhookNotifier = null)
    {
        parallelism = parallelism <= 0 ? Math.Max(1, Environment.ProcessorCount) : parallelism;

        if (!IODirectory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folderPath}");
        }

        IODirectory.CreateDirectory(indexPath);

        // When the index is protected by a master password (AppOptions.EncryptionEnabled), the
        // Lucene index folder is kept encrypted at rest as a single index.enc file. Before doing
        // any Lucene read/write below, decrypt it in-place into indexPath (a no-op if there is no
        // index.enc yet, e.g. first ever indexing run, or if it's already open/decrypted).
        byte[]? encryptionSalt = null;
        if (!string.IsNullOrEmpty(masterPassword) && !string.IsNullOrEmpty(masterKeyVerifier))
        {
            encryptionSalt = QuestResume.Core.Security.MasterKeyManager.ExtractSalt(masterKeyVerifier);
            LuceneIndexEncryptionService.OpenIntoWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

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

        // Documentos extraídos "de fresco" nesta corrida (não reaproveitados do manifesto
        // incremental), candidatos a resumo automático (AppOptions.AutoSummarizationEnabled).
        // Declarado neste escopo (fora dos "using" do writer) para ficar acessível na etapa de
        // sumarização, executada após o índice já ter sido confirmado e trocado.
        var newOrChangedDocs = new ConcurrentBag<(string Path, string FileName, string Text)>();

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
                var embeddingsAvailableFlag = _embeddingService is not null && _vectorStore is not null ? 1 : 0;

                // Wrap all vector inserts in a single SQLite transaction instead of one
                // commit per chunk, which dominates indexing time for large folders.
                using var batch = _vectorStore?.BeginBatch();

                // Maps content hash -> path of the first file with that content seen in this
                // run. Concurrent because files are now processed in parallel; TryAdd gives
                // atomic "first writer wins" duplicate detection.
                var seenHashes = new ConcurrentDictionary<string, string>();

                // Previous manifest (path -> fingerprint + cached chunks), used for incremental
                // indexing: files whose hash and last-write time haven't changed since the last
                // run are not re-extracted/re-chunked/re-embedded — their cached chunks are
                // reused as-is. The manifest sidecar lives directly in indexPath (not the temp
                // build dir) and is preserved across re-indexes, like tags.json.
                var previousManifest = incrementalIndexingEnabled
                    ? new IndexManifestRepository(indexPath).Load()
                    : new IndexManifest();
                var newManifest = new ConcurrentDictionary<string, ManifestFileEntry>();

                // Materialized upfront (instead of streamed via EnumerateFiles) so the total
                // file count is known, letting progress messages show "[i/total]" — a coarse
                // but real progress indicator for the CLI/Desktop status line.
                var allFiles = IODirectory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories).ToList();
                var fileCounter = 0;
                var statsLock = new object();
                var writerLock = new object();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(allFiles, parallelOptions, async (filePath, ct) =>
                {
                    var prefix = $"[{Interlocked.Increment(ref fileCounter)}/{allFiles.Count}]";

                    if (excludedFolders is not null && IsUnderExcludedFolder(filePath, excludedFolders))
                    {
                        lock (statsLock)
                        {
                            stats.FilesSkipped++;
                            stats.SkippedFiles.Add(filePath);
                        }
                        return;
                    }

                    var extension = Path.GetExtension(filePath);
                    if (!_registry.IsSupported(extension))
                    {
                        lock (statsLock)
                        {
                            stats.FilesSkipped++;
                            stats.SkippedFiles.Add(filePath);
                        }
                        return;
                    }

                    if (maxFileSizeBytes > 0 && new FileInfo(filePath).Length > maxFileSizeBytes)
                    {
                        lock (statsLock)
                        {
                            stats.FilesSkipped++;
                            stats.SkippedFiles.Add(filePath);
                        }
                        progress?.Report($"{prefix} Arquivo excede o tamanho máximo configurado, ignorado: {filePath}");
                        return;
                    }

                    try
                    {
                        var hash = await ComputeFileHashAsync(filePath, ct).ConfigureAwait(false);
                        if (!seenHashes.TryAdd(hash, filePath))
                        {
                            var originalPath = seenHashes[hash];
                            lock (statsLock)
                            {
                                stats.Duplicates.Add(new DuplicateFile { Path = filePath, DuplicateOfPath = originalPath });
                            }
                            progress?.Report($"{prefix} Duplicado (idêntico a {Path.GetFileName(originalPath)}): {filePath}");
                            return;
                        }

                        var fileInfo = new FileInfo(filePath);
                        var embeddingsAvailable = Volatile.Read(ref embeddingsAvailableFlag) == 1;

                        IReadOnlyList<TextChunk> chunks;
                        string fileName;
                        int chunkCountReused = 0;

                        if (incrementalIndexingEnabled
                            && previousManifest.Files.TryGetValue(filePath, out var previousEntry)
                            && previousEntry.Hash == hash
                            && previousEntry.LastWriteUtc == fileInfo.LastWriteTimeUtc
                            && previousEntry.Size == fileInfo.Length)
                        {
                            // Unchanged since the last run: reuse the cached chunks/embeddings
                            // instead of re-extracting and re-chunking the file.
                            fileName = previousEntry.Chunks.Count > 0 ? previousEntry.Chunks[0].FileName : fileInfo.Name;
                            chunkCountReused = previousEntry.Chunks.Count;

                            lock (writerLock)
                            {
                                foreach (var cachedChunk in previousEntry.Chunks)
                                {
                                    var chunk = new TextChunk
                                    {
                                        SourcePath = filePath,
                                        FileName = cachedChunk.FileName,
                                        ChunkIndex = cachedChunk.ChunkIndex,
                                        Text = cachedChunk.Text,
                                        ModifiedUtc = cachedChunk.ModifiedUtc
                                    };
                                    writer.AddDocument(ToLuceneDocument(chunk, cachedChunk.Text));

                                    if (embeddingsAvailable && cachedChunk.Embedding is not null)
                                    {
                                        _vectorStore!.Add(filePath, cachedChunk.FileName, cachedChunk.ChunkIndex, cachedChunk.Text, cachedChunk.Embedding);
                                    }
                                }
                            }

                            newManifest[filePath] = new ManifestFileEntry
                            {
                                Hash = hash,
                                LastWriteUtc = fileInfo.LastWriteTimeUtc,
                                Size = fileInfo.Length,
                                Chunks = previousEntry.Chunks
                            };

                            lock (statsLock)
                            {
                                stats.FilesProcessed++;
                                stats.ChunksIndexed += chunkCountReused;
                            }
                            progress?.Report($"{prefix} Inalterado, reaproveitado do índice anterior: {fileName} ({chunkCountReused} trecho(s))");
                            return;
                        }

                        var document = await _registry.ExtractAsync(filePath, ct).ConfigureAwait(false);

                        if (autoSummarizationEnabled && llmProvider is not null && !string.IsNullOrWhiteSpace(document.Text))
                        {
                            newOrChangedDocs.Add((filePath, document.FileName, document.Text));
                        }

                        chunks = TextChunker.CodeExtensions.Contains(extension)
                            ? TextChunker.ChunkCode(document, chunkSize, overlap)
                            : TextChunker.Chunk(document, chunkSize, overlap);
                        fileName = document.FileName;

                        var manifestChunks = incrementalIndexingEnabled ? new List<ManifestChunk>(chunks.Count) : null;

                        foreach (var chunk in chunks)
                        {
                            var content = piiRedactionEnabled ? PiiRedactor.Redact(chunk.Text) : chunk.Text;
                            float[]? embedding = null;

                            if (embeddingsAvailable)
                            {
                                try
                                {
                                    embedding = await _embeddingService!.EmbedAsync(content, ct).ConfigureAwait(false);
                                }
                                catch (EmbeddingsNotConfiguredException ex)
                                {
                                    Volatile.Write(ref embeddingsAvailableFlag, 0);
                                    embeddingsAvailable = false;
                                    progress?.Report($"Embeddings desabilitados durante a indexação: {ex.Message}");
                                }
                            }

                            lock (writerLock)
                            {
                                writer.AddDocument(ToLuceneDocument(chunk, content));
                                if (embedding is not null)
                                {
                                    _vectorStore!.Add(chunk.SourcePath, chunk.FileName, chunk.ChunkIndex, content, embedding);
                                }
                            }

                            manifestChunks?.Add(new ManifestChunk
                            {
                                ChunkIndex = chunk.ChunkIndex,
                                FileName = chunk.FileName,
                                Text = content,
                                ModifiedUtc = chunk.ModifiedUtc,
                                Embedding = embedding
                            });
                        }

                        if (incrementalIndexingEnabled)
                        {
                            newManifest[filePath] = new ManifestFileEntry
                            {
                                Hash = hash,
                                LastWriteUtc = fileInfo.LastWriteTimeUtc,
                                Size = fileInfo.Length,
                                Chunks = manifestChunks!
                            };
                        }

                        lock (statsLock)
                        {
                            stats.FilesProcessed++;
                            stats.ChunksIndexed += chunks.Count;
                        }
                        progress?.Report($"{prefix} Indexado: {fileName} ({chunks.Count} trecho(s))");

                        // Busca por imagem (CLIP): gera e armazena o embedding visual apenas para
                        // arquivos de imagem quando um modelo CLIP está configurado. Best-effort —
                        // ClipNotConfiguredException (modelo ausente/inválido) não deve abortar a
                        // indexação normal do arquivo, que já foi concluída acima.
                        if (_clipService is not null && _vectorStore is not null && ImageExtensions.Contains(extension))
                        {
                            try
                            {
                                var imageEmbedding = await _clipService.EmbedImageAsync(filePath, ct).ConfigureAwait(false);
                                lock (writerLock)
                                {
                                    _vectorStore.AddImageEmbedding(filePath, fileName, imageEmbedding);
                                }
                            }
                            catch (QuestResume.Core.Embeddings.ClipNotConfiguredException)
                            {
                                // Esperado quando ClipModelPath não está configurado; segue sem embedding visual.
                            }
                            catch (Exception ex)
                            {
                                progress?.Report($"{prefix} Falha ao gerar embedding CLIP para {fileName}: {ex.Message}");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lock (statsLock)
                        {
                            stats.Errors.Add($"{filePath}: {ex.Message}");
                        }
                        progress?.Report($"{prefix} Erro ao processar {filePath}: {ex.Message}");
                        webhookNotifier?.Notify("document.error", new { path = filePath, error = ex.Message });
                    }
                }).ConfigureAwait(false);

                if (incrementalIndexingEnabled)
                {
                    new IndexManifestRepository(indexPath).Save(new IndexManifest
                    {
                        Files = new Dictionary<string, ManifestFileEntry>(newManifest)
                    });
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

        // ON CLOSE: re-encrypt the freshly (re)built index back into index.enc and delete the
        // plaintext Lucene files, only after the indexing cycle above completed successfully.
        if (encryptionSalt is not null && !string.IsNullOrEmpty(masterPassword))
        {
            LuceneIndexEncryptionService.SealFromWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

        if (autoSummarizationEnabled && llmProvider is not null)
        {
            await GenerateSummariesAsync(indexPath, llmProvider, progress, cancellationToken).ConfigureAwait(false);
        }

        webhookNotifier?.Notify("indexing.completed", new
        {
            filesProcessed = stats.FilesProcessed,
            chunksIndexed = stats.ChunksIndexed,
            errors = stats.Errors.Count,
            duplicates = stats.Duplicates.Count
        });

        return stats;

        // Etapa separada, executada após a indexação principal já ter sido confirmada (commit +
        // swap), para que uma falha do LLM aqui nunca comprometa o índice recém-construído.
        async Task GenerateSummariesAsync(string idxPath, Rag.ILlmProvider provider, IProgress<string>? prog, CancellationToken ct)
        {
            if (newOrChangedDocs.IsEmpty) return;

            try
            {
                var repository = new Persistence.SummaryStoreRepository(idxPath);
                var store = repository.Load();
                var summarizer = new Rag.SummarizationService(provider);

                foreach (var (path, fileName, text) in newOrChangedDocs)
                {
                    try
                    {
                        var summary = await summarizer.SummarizeAsync(fileName, text, ct).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(summary))
                        {
                            store.SetSummary(path, summary);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        prog?.Report($"Falha ao gerar resumo automático para {fileName}: {ex.Message}");
                    }
                }

                repository.Save(store);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                prog?.Report($"Falha ao gerar resumos automáticos: {ex.Message}");
            }
        }
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

            // The incremental-indexing manifest is written directly to indexPath (not the temp
            // build dir) before the swap and must survive it, same as tags.json.
            if (fileName == IndexManifest.FileName)
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
