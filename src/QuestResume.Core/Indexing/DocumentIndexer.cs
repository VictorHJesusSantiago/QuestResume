using System.Collections.Concurrent;
using System.Security.Cryptography;
using Lucene.Net.Analysis.Br;
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
        WebhookNotifier? webhookNotifier = null,
        bool headingAwareChunkingEnabled = false,
        bool sentenceWindowChunkingEnabled = false,
        bool parentChildChunkingEnabled = false,
        int parentChunkSize = 1500,
        int childChunkSize = 200,
        bool semanticChunkingEnabled = false,
        double semanticChunkingThreshold = 0.5,
        bool contextualRetrievalEnabled = false,
        bool semanticDeduplicationEnabled = false,
        double semanticDuplicateThreshold = 0.97,
        IReadOnlyList<string>? additionalFolders = null)
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

        // Delta mode (AppOptions.IncrementalIndexingEnabled): the IndexWriter is opened directly
        // on indexPath in APPEND mode instead of rebuilding the whole index from scratch in a
        // temp dir + swap. Unchanged files (per the manifest) are skipped entirely — their
        // existing Lucene documents are left untouched. Changed/new files have their previous
        // documents deleted (by "path" term) before the fresh ones are added, and files removed
        // from disk since the last run have their documents deleted too. Falls back to the full
        // rebuild path below when disabled, preserving prior behaviour exactly.
        var deltaMode = incrementalIndexingEnabled;

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
        // the search index empty/corrupted for the rest of the session. Not used in delta mode,
        // which writes directly into indexPath instead.
        var tempIndexDir = Path.Combine(indexPath, "_index_tmp");
        if (!deltaMode)
        {
            if (IODirectory.Exists(tempIndexDir))
            {
                IODirectory.Delete(tempIndexDir, recursive: true);
            }

            IODirectory.CreateDirectory(tempIndexDir);
        }

        // Documentos extraídos "de fresco" nesta corrida (não reaproveitados do manifesto
        // incremental), candidatos a resumo automático (AppOptions.AutoSummarizationEnabled).
        // Declarado neste escopo (fora dos "using" do writer) para ficar acessível na etapa de
        // sumarização, executada após o índice já ter sido confirmado e trocado.
        var newOrChangedDocs = new ConcurrentBag<(string Path, string FileName, string Text)>();

        // Paths of files freshly extracted this run (not reused from the incremental-indexing
        // manifest), used by semantic deduplication (AppOptions.SemanticDeduplicationEnabled)
        // after the index is committed to know which documents to check against the rest.
        var freshlyIndexedPaths = new ConcurrentBag<string>();

        try
        {
            using (var directory = FSDirectory.Open(deltaMode ? indexPath : tempIndexDir))
            using (var analyzer = new BrazilianAnalyzer(MatchVersion))
            {
                var config = new IndexWriterConfig(MatchVersion, analyzer)
                {
                    OpenMode = deltaMode ? OpenMode.CREATE_OR_APPEND : OpenMode.CREATE
                };

                using var writer = new IndexWriter(directory, config);

                if (!deltaMode)
                {
                    _vectorStore?.Clear();
                }

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

                // Item 13 (arquivo movido/renomeado): mapa hash -> caminho antigo, construído uma
                // única vez antes do loop paralelo (somente leitura depois, sem necessidade de
                // lock). Usado para diferenciar "arquivo movido" (hash conhecido, caminho novo,
                // caminho antigo ausente em disco) de "arquivo realmente novo" — no primeiro caso
                // os chunks/embeddings cacheados são reaproveitados sem reprocessar.
                var previousPathByHash = new Dictionary<string, string>();
                if (incrementalIndexingEnabled)
                {
                    foreach (var kvp in previousManifest.Files)
                    {
                        previousPathByHash.TryAdd(kvp.Value.Hash, kvp.Key);
                    }
                }

                // Caminhos antigos já reaproveitados como "movidos" nesta corrida — excluídos da
                // contagem de "removido do índice" no bloco de limpeza do deltaMode abaixo, já que
                // seus documentos foram apenas realocados para o novo caminho, não descartados.
                var movedFromPaths = new ConcurrentDictionary<string, byte>();

                // Item 10 (.questresumeignore): carregado uma única vez por raiz (folderPath e,
                // se configuradas, AppOptions.AdditionalWatchedFolders — item 11), aplicado no
                // loop paralelo abaixo além de ExcludedFolders. Cada raiz adicional é escaneada
                // recursivamente e seus arquivos indexados com o caminho absoluto original
                // (documentos Lucene continuam apontando para fora de folderPath normalmente).
                var roots = new List<string> { folderPath };
                if (additionalFolders is not null)
                {
                    foreach (var extra in additionalFolders)
                    {
                        if (!string.IsNullOrWhiteSpace(extra)
                            && IODirectory.Exists(extra)
                            && !roots.Contains(extra, StringComparer.OrdinalIgnoreCase))
                        {
                            roots.Add(extra);
                        }
                    }
                }

                var ignoreMatcher = GitIgnoreMatcher.LoadFromFolder(folderPath);
                var ignoreMatchersByRoot = new Dictionary<string, GitIgnoreMatcher>(StringComparer.OrdinalIgnoreCase)
                {
                    [folderPath] = ignoreMatcher
                };
                foreach (var extra in roots.Skip(1))
                {
                    ignoreMatchersByRoot[extra] = GitIgnoreMatcher.LoadFromFolder(extra);
                }

                // Materialized upfront (instead of streamed via EnumerateFiles) so the total
                // file count is known, letting progress messages show "[i/total]" — a coarse
                // but real progress indicator for the CLI/Desktop status line. Each entry keeps
                // the root it came from, used below to resolve the right ignore matcher/relative
                // path (roots outside folderPath are scanned but never treated as "inside" it).
                var allFileEntries = new List<(string Path, string Root)>();
                foreach (var root in roots)
                {
                    foreach (var f in IODirectory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        allFileEntries.Add((f, root));
                    }
                }

                var allFiles = allFileEntries.Select(e => e.Path).ToList();
                var rootByFile = allFileEntries.ToDictionary(e => e.Path, e => e.Root, StringComparer.OrdinalIgnoreCase);
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

                    var fileRoot = rootByFile.TryGetValue(filePath, out var r) ? r : folderPath;
                    var fileIgnoreMatcher = ignoreMatchersByRoot.TryGetValue(fileRoot, out var im) ? im : ignoreMatcher;
                    if (fileIgnoreMatcher.HasRules)
                    {
                        var relativePath = Path.GetRelativePath(fileRoot, filePath);
                        if (fileIgnoreMatcher.IsIgnored(relativePath))
                        {
                            lock (statsLock)
                            {
                                stats.FilesSkipped++;
                                stats.SkippedFiles.Add(filePath);
                            }
                            return;
                        }
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
                            // Unchanged since the last run: in delta mode its Lucene documents
                            // and vector embeddings are already in the index/store from a
                            // previous run, so they're left completely untouched — only the full
                            // rebuild path (which starts from an empty index every time) needs to
                            // re-add them here.
                            fileName = previousEntry.Chunks.Count > 0 ? previousEntry.Chunks[0].FileName : fileInfo.Name;
                            chunkCountReused = previousEntry.Chunks.Count;

                            if (!deltaMode)
                            {
                                var reusedLanguage = LanguageDetector.Detect(
                                    string.Join(' ', previousEntry.Chunks.Select(c => c.Text)));

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
                                            ModifiedUtc = cachedChunk.ModifiedUtc,
                                            PageNumber = cachedChunk.PageNumber,
                                            ParentText = cachedChunk.ParentText
                                        };
                                        writer.AddDocument(ToLuceneDocument(chunk, cachedChunk.Text, previousEntry.Size, reusedLanguage));

                                        if (embeddingsAvailable && cachedChunk.Embedding is not null)
                                        {
                                            _vectorStore!.Add(filePath, cachedChunk.FileName, cachedChunk.ChunkIndex, cachedChunk.Text, cachedChunk.Embedding);
                                        }
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

                        // Item 13: arquivo movido/renomeado — mesmo hash de conteúdo já conhecido
                        // do manifesto anterior, mas em outro caminho, e esse caminho antigo não
                        // existe mais em disco. Reaproveita os chunks/embeddings cacheados sem
                        // reextrair/refragmentar/reembeddar, apenas realocando path.
                        if (incrementalIndexingEnabled
                            && previousPathByHash.TryGetValue(hash, out var oldPath)
                            && !string.Equals(oldPath, filePath, StringComparison.OrdinalIgnoreCase)
                            && !File.Exists(oldPath)
                            && previousManifest.Files.TryGetValue(oldPath, out var movedEntry))
                        {
                            fileName = movedEntry.Chunks.Count > 0 ? movedEntry.Chunks[0].FileName : fileInfo.Name;
                            chunkCountReused = movedEntry.Chunks.Count;
                            movedFromPaths.TryAdd(oldPath, 0);

                            var reusedLanguage = LanguageDetector.Detect(
                                string.Join(' ', movedEntry.Chunks.Select(c => c.Text)));

                            lock (writerLock)
                            {
                                if (deltaMode)
                                {
                                    writer.DeleteDocuments(new Term("path", oldPath));
                                }

                                foreach (var cachedChunk in movedEntry.Chunks)
                                {
                                    var chunk = new TextChunk
                                    {
                                        SourcePath = filePath,
                                        FileName = cachedChunk.FileName,
                                        ChunkIndex = cachedChunk.ChunkIndex,
                                        Text = cachedChunk.Text,
                                        ModifiedUtc = cachedChunk.ModifiedUtc,
                                        PageNumber = cachedChunk.PageNumber,
                                        ParentText = cachedChunk.ParentText
                                    };
                                    writer.AddDocument(ToLuceneDocument(chunk, cachedChunk.Text, movedEntry.Size, reusedLanguage));

                                    if (embeddingsAvailable && cachedChunk.Embedding is not null)
                                    {
                                        _vectorStore!.Add(filePath, cachedChunk.FileName, cachedChunk.ChunkIndex, cachedChunk.Text, cachedChunk.Embedding);
                                    }
                                }
                            }

                            if (deltaMode)
                            {
                                _vectorStore?.RemoveBySourcePath(oldPath);
                            }

                            newManifest[filePath] = new ManifestFileEntry
                            {
                                Hash = hash,
                                LastWriteUtc = fileInfo.LastWriteTimeUtc,
                                Size = fileInfo.Length,
                                Chunks = movedEntry.Chunks
                            };

                            lock (statsLock)
                            {
                                stats.FilesProcessed++;
                                stats.ChunksIndexed += chunkCountReused;
                            }
                            progress?.Report($"{prefix} Movido/renomeado (era {oldPath}), reaproveitado do índice anterior: {fileName} ({chunkCountReused} trecho(s))");
                            return;
                        }

                        if (deltaMode)
                        {
                            // File is new or changed since the last run: drop its previous
                            // Lucene documents (no-op if it wasn't indexed before) and vector
                            // embeddings before re-adding the fresh ones below, so the index
                            // never ends up with stale + fresh chunks for the same path.
                            lock (writerLock)
                            {
                                writer.DeleteDocuments(new Term("path", filePath));
                            }
                            _vectorStore?.RemoveBySourcePath(filePath);
                        }

                        var document = await _registry.ExtractAsync(filePath, ct).ConfigureAwait(false);
                        freshlyIndexedPaths.Add(filePath);

                        // Detecção de idioma (item 2): heurística leve baseada em stopwords
                        // características de cada idioma — ver LanguageDetector. Computada uma
                        // vez por documento (não por chunk) e gravada no campo Lucene "language"
                        // abaixo, usada por SearchFilters.Language para filtrar buscas.
                        var language = LanguageDetector.Detect(document.Text);

                        if (autoSummarizationEnabled && llmProvider is not null && !string.IsNullOrWhiteSpace(document.Text))
                        {
                            newOrChangedDocs.Add((filePath, document.FileName, document.Text));
                        }

                        // Contextual retrieval (AppOptions.ContextualRetrievalEnabled): a short
                        // (1-2 sentence) LLM-generated summary of the whole document, prefixed only
                        // to the text used for each chunk's embedding below — never to the stored/
                        // displayed chunk text itself. Best-effort: an LLM failure here just means
                        // chunks are embedded without the extra context, same as when disabled.
                        string? docContext = null;
                        if (contextualRetrievalEnabled && llmProvider is not null && !string.IsNullOrWhiteSpace(document.Text))
                        {
                            try
                            {
                                docContext = await new Rag.QueryEnhancementService(llmProvider)
                                    .GenerateShortDocumentContextAsync(document.FileName, document.Text, ct).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                progress?.Report($"{prefix} Contexto de indexação contextual não gerado para {document.FileName}: {ex.Message}");
                            }
                        }

                        // Ordem de precedência entre modos de chunking mutuamente exclusivos:
                        // 1) hierarquia de títulos (.md/.html, quando habilitado) — cada chunk fica
                        //    dentro dos limites de uma seção; 2) código-fonte (sempre, por extensão);
                        // 3) chunking semântico (quando habilitado e embeddings disponíveis);
                        // 4) parent-child; 5) janela de sentenças; 6) padrão por tamanho fixo.
                        if (headingAwareChunkingEnabled && TextChunker.HeadingAwareExtensions.Contains(extension))
                        {
                            chunks = TextChunker.ChunkByHeadings(document, chunkSize, overlap);
                        }
                        else if (TextChunker.CodeExtensions.Contains(extension))
                        {
                            chunks = TextChunker.ChunkCode(document, chunkSize, overlap);
                        }
                        else if (semanticChunkingEnabled && embeddingsAvailable)
                        {
                            chunks = await SemanticChunkAsync(document, semanticChunkingThreshold, chunkSize, ct).ConfigureAwait(false);
                        }
                        else if (parentChildChunkingEnabled)
                        {
                            chunks = TextChunker.ChunkParentChild(document, parentChunkSize, childChunkSize);
                        }
                        else if (sentenceWindowChunkingEnabled)
                        {
                            chunks = TextChunker.ChunkBySentences(document, chunkSize);
                        }
                        else
                        {
                            chunks = TextChunker.Chunk(document, chunkSize, overlap);
                        }

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
                                    var textForEmbedding = docContext is not null ? $"{docContext}\n\n{content}" : content;
                                    embedding = await _embeddingService!.EmbedAsync(textForEmbedding, ct).ConfigureAwait(false);
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
                                writer.AddDocument(ToLuceneDocument(chunk, content, fileInfo.Length, language));
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
                                Embedding = embedding,
                                PageNumber = chunk.PageNumber,
                                ParentText = chunk.ParentText
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

                if (deltaMode)
                {
                    // Files that were indexed last run but no longer exist on disk: delete their
                    // Lucene documents and vector embeddings, and drop them from the manifest so
                    // a later re-index doesn't try to diff against a stale entry.
                    foreach (var previousPath in previousManifest.Files.Keys)
                    {
                        if (newManifest.ContainsKey(previousPath) || File.Exists(previousPath) || movedFromPaths.ContainsKey(previousPath))
                        {
                            continue;
                        }

                        lock (writerLock)
                        {
                            writer.DeleteDocuments(new Term("path", previousPath));
                        }
                        _vectorStore?.RemoveBySourcePath(previousPath);
                        _vectorStore?.RemoveImageEmbedding(previousPath);

                        lock (statsLock)
                        {
                            stats.FilesRemoved++;
                        }
                        progress?.Report($"Removido do índice (não existe mais em disco): {previousPath}");
                    }
                }

                if (incrementalIndexingEnabled)
                {
                    new IndexManifestRepository(indexPath).Save(new IndexManifest
                    {
                        Files = new Dictionary<string, ManifestFileEntry>(newManifest)
                    });
                }

                writer.Commit();

                if (!deltaMode)
                {
                    // Re-indexing rebuilds the whole index from scratch (CREATE mode), which can
                    // leave many small segments. Merge down to a single segment for faster
                    // searches and a smaller on-disk footprint, then commit the merge. Skipped in
                    // delta mode: merging on every incremental run would rewrite the entire index
                    // and defeat the purpose of only touching changed documents.
                    writer.ForceMerge(1);
                    writer.Commit();
                }
            }

            if (!deltaMode)
            {
                SwapIndexDirectory(indexPath, tempIndexDir);
            }
        }
        finally
        {
            if (!deltaMode && IODirectory.Exists(tempIndexDir))
            {
                IODirectory.Delete(tempIndexDir, recursive: true);
            }
        }

        // Deduplicação semântica (AppOptions.SemanticDeduplicationEnabled): roda depois do commit/
        // swap acima, com o índice vetorial já refletindo o estado final desta corrida — best-
        // effort, nunca falha a indexação em si.
        if (semanticDeduplicationEnabled && _vectorStore is not null && !freshlyIndexedPaths.IsEmpty)
        {
            try
            {
                DetectNearDuplicates(freshlyIndexedPaths.ToHashSet(), semanticDuplicateThreshold, stats);
            }
            catch (Exception ex)
            {
                progress?.Report($"Falha ao detectar quase-duplicatas semânticas: {ex.Message}");
            }
        }

        new IndexReport
        {
            GeneratedUtc = DateTime.UtcNow,
            Errors = stats.Errors,
            Duplicates = stats.Duplicates,
            NearDuplicates = stats.NearDuplicates
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

    /// <summary>
    /// For every path in <paramref name="freshlyIndexedPaths"/>, computes its average chunk
    /// embedding and compares it (cosine similarity) against the average embedding of every
    /// other document currently in <see cref="_vectorStore"/>. Pairs at/above
    /// <paramref name="threshold"/> are appended to <see cref="IndexStats.NearDuplicates"/> —
    /// informational only, nothing is removed (see <see cref="NearDuplicateFile"/>).
    /// </summary>
    private void DetectNearDuplicates(HashSet<string> freshlyIndexedPaths, double threshold, IndexStats stats)
    {
        var entries = _vectorStore!.GetAllEntries();
        if (entries.Count == 0) return;

        var averageByPath = entries
            .GroupBy(e => e.Item.SourcePath)
            .ToDictionary(g => g.Key, g => Embeddings.EmbeddingMath.Average(g.Select(e => e.Embedding).ToList()));

        // Every path considered exactly once as the "new" side of a pair, compared against every
        // other path (both other fresh paths and pre-existing ones) — avoids symmetric duplicate
        // reports (A~B and B~A) only for pairs where both sides are fresh, by only ever emitting
        // the pair once (ordinal comparison breaks the tie).
        var reportedPairs = new HashSet<(string, string)>();

        foreach (var path in freshlyIndexedPaths)
        {
            if (!averageByPath.TryGetValue(path, out var avgA)) continue;

            foreach (var (otherPath, avgB) in averageByPath)
            {
                if (otherPath == path) continue;

                var isFreshPair = freshlyIndexedPaths.Contains(otherPath);
                if (isFreshPair && string.CompareOrdinal(path, otherPath) >= 0)
                {
                    // Only report fresh-fresh pairs once (from the lexicographically smaller path).
                    continue;
                }

                var pairKey = string.CompareOrdinal(path, otherPath) < 0 ? (path, otherPath) : (otherPath, path);
                if (!reportedPairs.Add(pairKey)) continue;

                var similarity = Embeddings.EmbeddingMath.CosineSimilarity(avgA, avgB);
                if (similarity >= threshold)
                {
                    stats.NearDuplicates.Add(new NearDuplicateFile
                    {
                        Path = path,
                        SimilarToPath = otherPath,
                        Similarity = similarity
                    });
                }
            }
        }
    }

    /// <summary>
    /// Reindexes a single file (item 12) without touching the rest of the index: deletes its
    /// existing Lucene documents (delete-by-<c>Term("path", filePath)</c>), re-extracts/chunks/
    /// embeds it, re-adds fresh documents, and — when <paramref name="incrementalIndexingEnabled"/>
    /// is set — updates just that file's entry in <c>index-manifest.json</c>. Returns the number
    /// of chunks written.
    /// </summary>
    public async Task<int> ReindexSingleFileAsync(
        string filePath,
        string indexPath,
        int chunkSize = 1000,
        int overlap = 150,
        bool piiRedactionEnabled = false,
        bool incrementalIndexingEnabled = false,
        string? masterPassword = null,
        string? masterKeyVerifier = null,
        bool headingAwareChunkingEnabled = false,
        bool sentenceWindowChunkingEnabled = false,
        bool parentChildChunkingEnabled = false,
        int parentChunkSize = 1500,
        int childChunkSize = 200,
        bool semanticChunkingEnabled = false,
        double semanticChunkingThreshold = 0.5,
        bool contextualRetrievalEnabled = false,
        Rag.ILlmProvider? llmProvider = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Arquivo não encontrado: {filePath}", filePath);
        }

        if (!IODirectory.Exists(indexPath))
        {
            throw new DirectoryNotFoundException($"Índice não encontrado: {indexPath}");
        }

        var extension = Path.GetExtension(filePath);
        if (!_registry.IsSupported(extension))
        {
            throw new NotSupportedException($"Extensão não suportada: {extension}");
        }

        byte[]? encryptionSalt = null;
        if (!string.IsNullOrEmpty(masterPassword) && !string.IsNullOrEmpty(masterKeyVerifier))
        {
            encryptionSalt = QuestResume.Core.Security.MasterKeyManager.ExtractSalt(masterKeyVerifier);
            LuceneIndexEncryptionService.OpenIntoWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

        var fileInfo = new FileInfo(filePath);
        var chunkCount = 0;

        using (var directory = FSDirectory.Open(indexPath))
        using (var analyzer = new BrazilianAnalyzer(MatchVersion))
        {
            var config = new IndexWriterConfig(MatchVersion, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND };
            using var writer = new IndexWriter(directory, config);

            writer.DeleteDocuments(new Term("path", filePath));
            _vectorStore?.RemoveBySourcePath(filePath);

            var document = await _registry.ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);
            var language = LanguageDetector.Detect(document.Text);
            var embeddingsAvailable = _embeddingService is not null && _vectorStore is not null;

            string? docContext = null;
            if (contextualRetrievalEnabled && llmProvider is not null && !string.IsNullOrWhiteSpace(document.Text))
            {
                try
                {
                    docContext = await new Rag.QueryEnhancementService(llmProvider)
                        .GenerateShortDocumentContextAsync(document.FileName, document.Text, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Best-effort, same as IndexFolderAsync.
                }
            }

            IReadOnlyList<TextChunk> chunks;
            if (headingAwareChunkingEnabled && TextChunker.HeadingAwareExtensions.Contains(extension))
            {
                chunks = TextChunker.ChunkByHeadings(document, chunkSize, overlap);
            }
            else if (TextChunker.CodeExtensions.Contains(extension))
            {
                chunks = TextChunker.ChunkCode(document, chunkSize, overlap);
            }
            else if (semanticChunkingEnabled && embeddingsAvailable)
            {
                chunks = await SemanticChunkAsync(document, semanticChunkingThreshold, chunkSize, cancellationToken).ConfigureAwait(false);
            }
            else if (parentChildChunkingEnabled)
            {
                chunks = TextChunker.ChunkParentChild(document, parentChunkSize, childChunkSize);
            }
            else if (sentenceWindowChunkingEnabled)
            {
                chunks = TextChunker.ChunkBySentences(document, chunkSize);
            }
            else
            {
                chunks = TextChunker.Chunk(document, chunkSize, overlap);
            }

            var manifestChunks = incrementalIndexingEnabled ? new List<ManifestChunk>(chunks.Count) : null;

            using (_vectorStore?.BeginBatch())
            {
                foreach (var chunk in chunks)
                {
                    var content = piiRedactionEnabled ? PiiRedactor.Redact(chunk.Text) : chunk.Text;
                    float[]? embedding = null;

                    if (embeddingsAvailable)
                    {
                        var textForEmbedding = docContext is not null ? $"{docContext}\n\n{content}" : content;
                        embedding = await _embeddingService!.EmbedAsync(textForEmbedding, cancellationToken).ConfigureAwait(false);
                    }

                    writer.AddDocument(ToLuceneDocument(chunk, content, fileInfo.Length, language));
                    if (embedding is not null)
                    {
                        _vectorStore!.Add(chunk.SourcePath, chunk.FileName, chunk.ChunkIndex, content, embedding);
                    }

                    manifestChunks?.Add(new ManifestChunk
                    {
                        ChunkIndex = chunk.ChunkIndex,
                        FileName = chunk.FileName,
                        Text = content,
                        ModifiedUtc = chunk.ModifiedUtc,
                        Embedding = embedding,
                        PageNumber = chunk.PageNumber,
                        ParentText = chunk.ParentText
                    });

                    chunkCount++;
                }
            }

            if (incrementalIndexingEnabled)
            {
                var repository = new IndexManifestRepository(indexPath);
                var manifest = repository.Load();
                manifest.Files[filePath] = new ManifestFileEntry
                {
                    Hash = await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false),
                    LastWriteUtc = fileInfo.LastWriteTimeUtc,
                    Size = fileInfo.Length,
                    Chunks = manifestChunks ?? new List<ManifestChunk>()
                };
                repository.Save(manifest);
            }

            writer.Commit();
        }

        if (encryptionSalt is not null && !string.IsNullOrEmpty(masterPassword))
        {
            LuceneIndexEncryptionService.SealFromWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

        return chunkCount;
    }

    /// <summary>
    /// Removes documents from <paramref name="indexPath"/> whose source file no longer exists on
    /// disk (item 14). Reads the Lucene index directly (<see cref="DirectoryReader"/>) to discover
    /// every distinct "path" value currently stored, checks which no longer exist, and deletes
    /// their chunks from the Lucene index, the vector store (text + image embeddings) and the
    /// incremental-indexing manifest, if present. Returns the number of orphaned files removed.
    /// Unlike the automatic cleanup that happens during a normal delta-mode
    /// <see cref="IndexFolderAsync"/> run, this works standalone (e.g. for a full-rebuild index,
    /// which doesn't do this automatically, or without triggering a full re-index at all).
    /// </summary>
    public Task<int> CleanOrphansAsync(string indexPath, CancellationToken cancellationToken = default)
    {
        if (!IODirectory.Exists(indexPath))
        {
            throw new DirectoryNotFoundException($"Índice não encontrado: {indexPath}");
        }

        var orphanPaths = new List<string>();

        using (var directory = FSDirectory.Open(indexPath))
        {
            if (!DirectoryReader.IndexExists(directory))
            {
                return Task.FromResult(0);
            }

            using var reader = DirectoryReader.Open(directory);
            var paths = new HashSet<string>();

            // MaxDoc includes documents already marked deleted by a previous DeleteDocuments call
            // that haven't been physically purged yet (no merge/forceMerge happened) — skip them
            // via the live-docs bitset, otherwise a second CleanOrphansAsync run over the same
            // (uncommitted-merge) index would re-report already-removed files as still orphaned.
            var liveDocs = MultiFields.GetLiveDocs(reader);

            for (var i = 0; i < reader.MaxDoc; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (liveDocs is not null && !liveDocs.Get(i))
                {
                    continue;
                }

                var doc = reader.Document(i);
                var path = doc.Get("path");
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    orphanPaths.Add(path);
                }
            }
        }

        if (orphanPaths.Count == 0)
        {
            return Task.FromResult(0);
        }

        using (var directory = FSDirectory.Open(indexPath))
        using (var analyzer = new BrazilianAnalyzer(MatchVersion))
        {
            var config = new IndexWriterConfig(MatchVersion, analyzer) { OpenMode = OpenMode.CREATE_OR_APPEND };
            using var writer = new IndexWriter(directory, config);

            foreach (var path in orphanPaths)
            {
                writer.DeleteDocuments(new Term("path", path));
                _vectorStore?.RemoveBySourcePath(path);
                _vectorStore?.RemoveImageEmbedding(path);
            }

            writer.Commit();
        }

        var manifestRepository = new IndexManifestRepository(indexPath);
        var manifest = manifestRepository.Load();
        var manifestChanged = false;
        foreach (var path in orphanPaths)
        {
            if (manifest.Files.Remove(path))
            {
                manifestChanged = true;
            }
        }

        if (manifestChanged)
        {
            manifestRepository.Save(manifest);
        }

        return Task.FromResult(orphanPaths.Count);
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

    private static Document ToLuceneDocument(TextChunk chunk, string content, long sizeBytes = 0, string? language = null)
    {
        var doc = new Document
        {
            new StringField("path", chunk.SourcePath, Field.Store.YES),
            new StringField("fileName", chunk.FileName, Field.Store.YES),
            new StringField("extension", Path.GetExtension(chunk.FileName).ToLowerInvariant(), Field.Store.YES),
            new StoredField("chunkIndex", chunk.ChunkIndex.ToString()),
            new TextField("content", content, Field.Store.YES),
            new StringField("modifiedUtc", chunk.ModifiedUtc.Ticks.ToString(), Field.Store.YES),
            // Indexed both as a sortable numeric doc-value field and stored for filtering/display.
            new Int64Field("modifiedUtcTicks", chunk.ModifiedUtc.Ticks, Field.Store.NO),
            new NumericDocValuesField("modifiedUtcTicks", chunk.ModifiedUtc.Ticks),
            new StoredField("sizeBytes", sizeBytes),
            new Int64Field("sizeBytesIndexed", sizeBytes, Field.Store.NO),
            // Idioma detectado do documento (item 2, ver LanguageDetector) — indexado como campo
            // exato para permitir filtro por idioma em SearchFilters.Language.
            new StringField("language", language ?? LanguageDetector.Unknown, Field.Store.YES)
        };

        if (chunk.PageNumber.HasValue)
        {
            doc.Add(new StoredField("pageNumber", chunk.PageNumber.Value.ToString()));
        }

        if (!string.IsNullOrEmpty(chunk.ParentText))
        {
            // Stored only (not indexed/searched) — parent-child chunking (item 6) searches over
            // the small "content" field above, but SearchService.Search substitutes this larger
            // parent text back in as ChunkText for display/LLM-prompt purposes.
            doc.Add(new StoredField("parentText", chunk.ParentText));
        }

        return doc;
    }

    /// <summary>
    /// Chunking semântico (<see cref="Configuration.AppOptions.SemanticChunkingEnabled"/>): quebra
    /// o texto em sentenças, calcula o embedding de cada uma, e agrupa sentenças consecutivas no
    /// mesmo chunk enquanto a similaridade de cosseno entre elas permanecer acima de
    /// <paramref name="threshold"/> — assim, sentenças semanticamente coesas ficam no mesmo chunk
    /// em vez de serem cortadas por tamanho fixo. Um novo chunk também é iniciado se o acumulado
    /// ultrapassaria <paramref name="chunkSize"/>. Requer <see cref="_embeddingService"/> — só
    /// deve ser chamado quando embeddings estão disponíveis (ver chamador em
    /// <see cref="IndexFolderAsync"/>).
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> SemanticChunkAsync(
        ExtractedDocument document, double threshold, int chunkSize, CancellationToken cancellationToken)
    {
        var sentences = TextChunker.SplitSentences(document.Text);
        if (sentences.Count == 0) return Array.Empty<TextChunk>();

        if (sentences.Count == 1)
        {
            return new List<TextChunk>
            {
                new()
                {
                    SourcePath = document.Path,
                    FileName = document.FileName,
                    ChunkIndex = 0,
                    Text = sentences[0],
                    ModifiedUtc = document.ModifiedUtc
                }
            };
        }

        var embeddings = new float[sentences.Count][];
        for (var i = 0; i < sentences.Count; i++)
        {
            embeddings[i] = await _embeddingService!.EmbedAsync(sentences[i], cancellationToken).ConfigureAwait(false);
        }

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;
        var currentSentences = new List<string> { sentences[0] };
        var currentLength = sentences[0].Length;

        void FlushCurrent()
        {
            var text = string.Join(' ', currentSentences).Trim();
            if (text.Length == 0) return;

            chunks.Add(new TextChunk
            {
                SourcePath = document.Path,
                FileName = document.FileName,
                ChunkIndex = chunkIndex++,
                Text = text,
                ModifiedUtc = document.ModifiedUtc
            });
        }

        for (var i = 1; i < sentences.Count; i++)
        {
            var similarity = QuestResume.Core.Embeddings.EmbeddingMath.CosineSimilarity(embeddings[i - 1], embeddings[i]);
            var wouldExceedSize = currentLength + 1 + sentences[i].Length > chunkSize;

            if (similarity < threshold || wouldExceedSize)
            {
                FlushCurrent();
                currentSentences = new List<string> { sentences[i] };
                currentLength = sentences[i].Length;
            }
            else
            {
                currentSentences.Add(sentences[i]);
                currentLength += 1 + sentences[i].Length;
            }
        }

        FlushCurrent();

        return chunks;
    }
}
