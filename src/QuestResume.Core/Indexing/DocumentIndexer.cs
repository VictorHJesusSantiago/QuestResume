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
        IReadOnlyList<string>? additionalFolders = null,
        int throttleDelayMs = 0,
        bool prioritizeRecentFiles = false,
        System.Threading.ManualResetEventSlim? pauseHandle = null,
        bool entityExtractionEnabled = false,
        bool documentVersioningEnabled = false)
    {
        parallelism = parallelism <= 0 ? Math.Max(1, Environment.ProcessorCount) : parallelism;

        if (!IODirectory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Pasta não encontrada: {folderPath}");
        }

        IODirectory.CreateDirectory(indexPath);

        
        
        
        
        byte[]? encryptionSalt = null;
        if (!string.IsNullOrEmpty(masterPassword) && !string.IsNullOrEmpty(masterKeyVerifier))
        {
            encryptionSalt = QuestResume.Core.Security.MasterKeyManager.ExtractSalt(masterKeyVerifier);
            LuceneIndexEncryptionService.OpenIntoWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

        var stats = new IndexStats();

        
        
        
        
        
        
        
        var deltaMode = incrementalIndexingEnabled;

        
        
        
        var swapMarkerPath = Path.Combine(indexPath, "_swap_pending.marker");
        if (File.Exists(swapMarkerPath))
        {
            File.Delete(swapMarkerPath);
        }

        
        
        
        
        
        var tempIndexDir = Path.Combine(indexPath, "_index_tmp");
        if (!deltaMode)
        {
            if (IODirectory.Exists(tempIndexDir))
            {
                IODirectory.Delete(tempIndexDir, recursive: true);
            }

            IODirectory.CreateDirectory(tempIndexDir);
        }

        
        
        
        
        var newOrChangedDocs = new ConcurrentBag<(string Path, string FileName, string Text)>();

        
        
        
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

                
                
                using var batch = _vectorStore?.BeginBatch();

                
                
                
                var seenHashes = new ConcurrentDictionary<string, string>();

                
                
                
                
                
                var previousManifest = incrementalIndexingEnabled
                    ? new IndexManifestRepository(indexPath).Load()
                    : new IndexManifest();
                var newManifest = new ConcurrentDictionary<string, ManifestFileEntry>();

                
                
                
                
                
                var previousPathByHash = new Dictionary<string, string>();
                if (incrementalIndexingEnabled)
                {
                    foreach (var kvp in previousManifest.Files)
                    {
                        previousPathByHash.TryAdd(kvp.Value.Hash, kvp.Key);
                    }
                }

                
                
                
                var movedFromPaths = new ConcurrentDictionary<string, byte>();

                
                
                
                
                
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

                
                
                
                
                
                var allFileEntries = new List<(string Path, string Root)>();
                foreach (var root in roots)
                {
                    foreach (var f in IODirectory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        allFileEntries.Add((f, root));
                    }
                }

                
                
                
                
                
                if (prioritizeRecentFiles)
                {
                    allFileEntries = allFileEntries
                        .OrderByDescending(e => File.GetLastWriteTimeUtc(e.Path))
                        .ToList();
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
                    
                    
                    
                    
                    
                    
                    pauseHandle?.Wait(ct);

                    
                    
                    
                    if (throttleDelayMs > 0)
                    {
                        await Task.Delay(throttleDelayMs, ct).ConfigureAwait(false);
                    }

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
                            
                            
                            
                            
                            lock (writerLock)
                            {
                                writer.DeleteDocuments(new Term("path", filePath));
                            }
                            _vectorStore?.RemoveBySourcePath(filePath);
                        }

                        var document = await _registry.ExtractAsync(filePath, ct).ConfigureAwait(false);
                        freshlyIndexedPaths.Add(filePath);

                        
                        
                        
                        
                        var language = LanguageDetector.Detect(document.Text);

                        
                        
                        
                        
                        var needsDocText = (autoSummarizationEnabled && llmProvider is not null)
                            || (entityExtractionEnabled && llmProvider is not null)
                            || documentVersioningEnabled;
                        if (needsDocText && !string.IsNullOrWhiteSpace(document.Text))
                        {
                            newOrChangedDocs.Add((filePath, document.FileName, document.Text));
                        }

                        
                        
                        
                        
                        
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

        
        
        if (encryptionSalt is not null && !string.IsNullOrEmpty(masterPassword))
        {
            LuceneIndexEncryptionService.SealFromWorkingFolder(indexPath, indexPath, masterPassword, encryptionSalt);
        }

        if (autoSummarizationEnabled && llmProvider is not null)
        {
            await GenerateSummariesAsync(indexPath, llmProvider, progress, cancellationToken).ConfigureAwait(false);
        }

        
        
        
        if (entityExtractionEnabled && llmProvider is not null)
        {
            await ExtractEntitiesAsync(indexPath, llmProvider, progress, cancellationToken).ConfigureAwait(false);
        }

        
        
        if (documentVersioningEnabled)
        {
            SaveDocumentVersions(indexPath, progress);
        }

        webhookNotifier?.Notify("indexing.completed", new
        {
            filesProcessed = stats.FilesProcessed,
            chunksIndexed = stats.ChunksIndexed,
            errors = stats.Errors.Count,
            duplicates = stats.Duplicates.Count
        });

        return stats;

        
        
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

        async Task ExtractEntitiesAsync(string idxPath, Rag.ILlmProvider provider, IProgress<string>? prog, CancellationToken ct)
        {
            if (newOrChangedDocs.IsEmpty) return;

            try
            {
                var store = new Persistence.EntityStore(idxPath);
                var extractor = new Rag.EntityExtractionService(provider);

                foreach (var (path, fileName, text) in newOrChangedDocs)
                {
                    try
                    {
                        var entities = await extractor.ExtractAsync(text, ct).ConfigureAwait(false);
                        if (entities.Count > 0)
                        {
                            store.SetEntities(path, entities);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        prog?.Report($"Falha ao extrair entidades de {fileName}: {ex.Message}");
                    }
                }

                store.Save();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                prog?.Report($"Falha ao extrair entidades: {ex.Message}");
            }
        }

        void SaveDocumentVersions(string idxPath, IProgress<string>? prog)
        {
            if (newOrChangedDocs.IsEmpty) return;

            try
            {
                var store = new Persistence.DocumentVersionStore(idxPath);
                var anyChanged = false;
                foreach (var (path, fileName, text) in newOrChangedDocs)
                {
                    try
                    {
                        if (store.SaveVersion(path, text))
                        {
                            anyChanged = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        prog?.Report($"Falha ao versionar {fileName}: {ex.Message}");
                    }
                }

                if (anyChanged)
                {
                    store.Save();
                }
            }
            catch (Exception ex)
            {
                prog?.Report($"Falha ao versionar documentos: {ex.Message}");
            }
        }
    }

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

        private void DetectNearDuplicates(HashSet<string> freshlyIndexedPaths, double threshold, IndexStats stats)
    {
        var entries = _vectorStore!.GetAllEntries();
        if (entries.Count == 0) return;

        var averageByPath = entries
            .GroupBy(e => e.Item.SourcePath)
            .ToDictionary(g => g.Key, g => Embeddings.EmbeddingMath.Average(g.Select(e => e.Embedding).ToList()));

        
        
        
        
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

        private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private const string SwapMarkerFileName = "_swap_pending.marker";

        private static void SwapIndexDirectory(string indexPath, string tempIndexDir)
    {
        var markerPath = Path.Combine(indexPath, SwapMarkerFileName);
        File.WriteAllText(markerPath, string.Empty);

        foreach (var file in IODirectory.GetFiles(indexPath))
        {
            var fileName = Path.GetFileName(file);

            
            
            if (fileName.StartsWith(VectorStore.DatabaseFileName, StringComparison.Ordinal))
            {
                continue;
            }

            
            
            if (fileName == TagStore.FileName)
            {
                continue;
            }

            
            
            if (fileName == IndexManifest.FileName)
            {
                continue;
            }

            
            
            if (fileName == AuditLog.FileName)
            {
                continue;
            }

            
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
            
            new Int64Field("modifiedUtcTicks", chunk.ModifiedUtc.Ticks, Field.Store.NO),
            new NumericDocValuesField("modifiedUtcTicks", chunk.ModifiedUtc.Ticks),
            new StoredField("sizeBytes", sizeBytes),
            new Int64Field("sizeBytesIndexed", sizeBytes, Field.Store.NO),
            
            
            new StringField("language", language ?? LanguageDetector.Unknown, Field.Store.YES)
        };

        if (chunk.PageNumber.HasValue)
        {
            doc.Add(new StoredField("pageNumber", chunk.PageNumber.Value.ToString()));
        }

        if (!string.IsNullOrEmpty(chunk.ParentText))
        {
            
            
            
            doc.Add(new StoredField("parentText", chunk.ParentText));
        }

        return doc;
    }

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
