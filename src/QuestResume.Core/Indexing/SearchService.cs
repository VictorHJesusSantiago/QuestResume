using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Store;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Optional metadata filters applied alongside the full-text query in
/// <see cref="SearchService.Search"/>.
/// </summary>
/// <param name="Extension">
/// File extension to restrict results to (e.g. <c>".pdf"</c> or <c>"pdf"</c>), case-insensitive.
/// </param>
/// <param name="FolderPath">
/// Restricts results to files whose indexed path starts with this prefix.
/// </param>
/// <param name="Tag">
/// Restricts results to files that have this user-assigned tag (see <see cref="TagStore"/>),
/// case-insensitive. Applied as a post-filter since tags aren't stored in the Lucene index.
/// </param>
public sealed record SearchFilters(string? Extension = null, string? FolderPath = null, string? Tag = null)
{
    public bool HasAny => !string.IsNullOrWhiteSpace(Extension) || !string.IsNullOrWhiteSpace(FolderPath) || !string.IsNullOrWhiteSpace(Tag);
}

/// <summary>
/// Runs BM25 full-text queries against the Lucene.NET index produced by
/// <see cref="DocumentIndexer"/>.
///
/// Accepts an optional <see cref="LuceneIndexManager"/> (inject via DI in the API). When
/// provided, the underlying <see cref="DirectoryReader"/> is shared across requests and
/// refreshed via <c>OpenIfChanged</c> instead of being opened and closed per call — removes
/// O(requests × endpoints) file-descriptor pressure and recovers the Lucene segment cache.
/// When not provided (Desktop/CLI) the existing per-call open/close behaviour is preserved.
/// </summary>
public sealed class SearchService : ISearchService
{
    /// <summary>
    /// When a tag filter is applied, fetch this many times <c>topK</c> BM25 candidates before
    /// filtering by tag, since tags aren't stored in the Lucene index.
    /// </summary>
    private const int TagFilterCandidateMultiplier = 5;

    private readonly string _indexPath;
    private readonly LuceneIndexManager? _indexManager;
    private readonly string? _masterPassword;
    private readonly byte[]? _encryptionSalt;
    private readonly QuestResume.Core.Embeddings.IVectorStore? _vectorStore;
    private readonly QuestResume.Core.Embeddings.IClipEmbeddingService? _clipService;

    public SearchService(string indexPath, LuceneIndexManager? indexManager = null)
        : this(indexPath, indexManager, masterPassword: null, masterKeyVerifier: null)
    {
    }

    /// <summary>
    /// Overload usado quando busca por similaridade de imagem (CLIP) está disponível. Sem
    /// <paramref name="vectorStore"/>/<paramref name="clipService"/>, <see cref="SearchByImageAsync"/>
    /// lança <see cref="QuestResume.Core.Embeddings.ClipNotConfiguredException"/>.
    /// </summary>
    public SearchService(
        string indexPath,
        LuceneIndexManager? indexManager,
        QuestResume.Core.Embeddings.IVectorStore? vectorStore,
        QuestResume.Core.Embeddings.IClipEmbeddingService? clipService)
        : this(indexPath, indexManager, masterPassword: null, masterKeyVerifier: null)
    {
        _vectorStore = vectorStore;
        _clipService = clipService;
    }

    /// <summary>
    /// Overload used when the index at <paramref name="indexPath"/> is protected by a master
    /// password (<c>AppOptions.EncryptionEnabled</c>). ON OPEN: if an <c>index.enc</c> file
    /// exists and plain Lucene segment files aren't already present, it is decrypted in-place
    /// into <paramref name="indexPath"/> before any read below. Call <see cref="SealAsync"/>
    /// explicitly once done searching to re-encrypt and remove the plaintext again.
    /// </summary>
    public SearchService(string indexPath, LuceneIndexManager? indexManager, string? masterPassword, string? masterKeyVerifier)
    {
        _indexPath = indexPath;
        _indexManager = indexManager;
        _masterPassword = masterPassword;

        if (!string.IsNullOrEmpty(masterPassword) && !string.IsNullOrEmpty(masterKeyVerifier))
        {
            _encryptionSalt = QuestResume.Core.Security.MasterKeyManager.ExtractSalt(masterKeyVerifier);
            LuceneIndexEncryptionService.OpenIntoWorkingFolder(indexPath, indexPath, masterPassword, _encryptionSalt);
        }
    }

    /// <summary>
    /// Explicit close/seal point for callers that constructed this <see cref="SearchService"/>
    /// with a master password: re-encrypts <see cref="_indexPath"/> back into <c>index.enc</c>
    /// and deletes the plaintext Lucene files. No-op if encryption wasn't configured.
    /// </summary>
    public Task SealAsync()
    {
        if (_encryptionSalt is not null && !string.IsNullOrEmpty(_masterPassword))
        {
            return LuceneIndexEncryptionService.SealAsync(_indexPath, _indexPath, _masterPassword, _encryptionSalt);
        }

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Reader acquisition helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a reader handle: either a borrowed shared reader (from the manager, must NOT
    /// be disposed) or a freshly opened owned reader (must be disposed after use).
    /// </summary>
    private ReaderHandle OpenReader()
    {
        if (_indexManager is not null)
        {
            var shared = _indexManager.AcquireReader(_indexPath);
            return shared is not null ? ReaderHandle.Borrowed(shared) : default;
        }

        if (!IODirectory.Exists(_indexPath)) return default;

        FSDirectory? dir = null;
        try
        {
            dir = FSDirectory.Open(_indexPath);
            if (!DirectoryReader.IndexExists(dir))
            {
                dir.Dispose();
                return default;
            }

            return ReaderHandle.Owned(dir, DirectoryReader.Open(dir));
        }
        catch
        {
            dir?.Dispose();
            return default;
        }
    }

    /// <summary>
    /// Lightweight disposable wrapper that tracks whether this service owns the reader
    /// (must dispose) or merely borrowed it from <see cref="LuceneIndexManager"/> (must not).
    /// </summary>
    private readonly struct ReaderHandle : IDisposable
    {
        public readonly DirectoryReader? Reader;
        private readonly FSDirectory? _ownedDir;
        private readonly DirectoryReader? _ownedReader;

        private ReaderHandle(DirectoryReader reader, FSDirectory? ownedDir, DirectoryReader? ownedReader)
        {
            Reader = reader;
            _ownedDir = ownedDir;
            _ownedReader = ownedReader;
        }

        public static ReaderHandle Borrowed(DirectoryReader reader) =>
            new(reader, null, null);

        public static ReaderHandle Owned(FSDirectory dir, DirectoryReader reader) =>
            new(reader, dir, reader);

        public void Dispose()
        {
            _ownedReader?.Dispose();
            _ownedDir?.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // TagStore access — uses manager cache when available
    // -------------------------------------------------------------------------

    private TagStore LoadTagStore()
    {
        if (_indexManager is not null)
            return _indexManager.GetTagStore(_indexPath);

        return TagStore.Load(_indexPath);
    }

    private void SaveTagStoreAndInvalidate(TagStore store)
    {
        store.Save(_indexPath);
        _indexManager?.InvalidateTagStore();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public bool IndexExists()
    {
        if (_indexManager is not null)
            return _indexManager.IndexExists(_indexPath);

        if (!IODirectory.Exists(_indexPath)) return false;
        using var directory = FSDirectory.Open(_indexPath);
        return DirectoryReader.IndexExists(directory);
    }

    public int GetDocumentCount()
    {
        if (!IndexExists()) return 0;

        using var handle = OpenReader();
        if (handle.Reader is null) return 0;
        return handle.Reader.NumDocs;
    }

    /// <summary>
    /// Returns every indexed chunk for the given source file path, ordered by chunk index.
    /// Used by <see cref="QuestResume.Core.Rag.RagQueryEngine.CompareAsync"/> to retrieve a
    /// whole document's content rather than just the chunks matching a query.
    /// </summary>
    public IReadOnlyList<SearchResultItem> GetChunksByPath(string path)
    {
        if (!IndexExists()) return Array.Empty<SearchResultItem>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<SearchResultItem>();

        var searcher = new IndexSearcher(handle.Reader);
        var query = new TermQuery(new Term("path", path));
        var hits = searcher.Search(query, Math.Max(handle.Reader.MaxDoc, 1));

        var results = new List<SearchResultItem>(hits.ScoreDocs.Length);
        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            results.Add(new SearchResultItem
            {
                SourcePath = doc.Get("path") ?? string.Empty,
                FileName = doc.Get("fileName") ?? string.Empty,
                ChunkIndex = int.TryParse(doc.Get("chunkIndex"), out var chunkIndex) ? chunkIndex : 0,
                ChunkText = doc.Get("content") ?? string.Empty,
                Score = scoreDoc.Score
            });
        }

        return results.OrderBy(r => r.ChunkIndex).ToList();
    }

    /// <summary>
    /// Lists source files currently present in the index with chunk counts and tags.
    /// </summary>
    /// <param name="skip">Number of files to skip (for pagination). 0 = start from the beginning.</param>
    /// <param name="take">Maximum files to return. 0 = return all.</param>
    public IReadOnlyList<IndexedFileInfo> GetIndexedFiles(int skip = 0, int take = 0)
    {
        if (!IndexExists()) return Array.Empty<IndexedFileInfo>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<IndexedFileInfo>();

        var searcher = new IndexSearcher(handle.Reader);
        // Cap to MaxDoc so MatchAllDocsQuery doesn't silently truncate on very large indexes.
        var hits = searcher.Search(new MatchAllDocsQuery(), Math.Max(handle.Reader.MaxDoc, 1));
        var tagStore = LoadTagStore();
        var summaryStore = new Persistence.SummaryStoreRepository(_indexPath).Load();

        var files = new Dictionary<string, IndexedFileInfo>();
        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var path = doc.Get("path") ?? string.Empty;
            if (string.IsNullOrEmpty(path)) continue;

            if (!files.TryGetValue(path, out var info))
            {
                info = new IndexedFileInfo
                {
                    SourcePath = path,
                    FileName = doc.Get("fileName") ?? string.Empty,
                    Tags = tagStore.GetTags(path).ToList(),
                    Summary = summaryStore.GetSummary(path)
                };
                files[path] = info;
            }

            info.ChunkCount++;
        }

        var ordered = files.Values.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase);
        IEnumerable<IndexedFileInfo> paged = skip > 0 ? ordered.Skip(skip) : ordered;
        if (take > 0) paged = paged.Take(take);
        return paged.ToList();
    }

    /// <summary>Returns the tags currently assigned to <paramref name="sourcePath"/>.</summary>
    public IReadOnlyList<string> GetTags(string sourcePath) => LoadTagStore().GetTags(sourcePath);

    /// <summary>Replaces the tags assigned to <paramref name="sourcePath"/>.</summary>
    public void SetTags(string sourcePath, IEnumerable<string> tags)
    {
        var store = LoadTagStore();
        store.SetTags(sourcePath, tags);
        SaveTagStoreAndInvalidate(store);
    }

    /// <summary>Returns every distinct tag assigned to any document, for building filter UIs.</summary>
    public IReadOnlyList<string> GetAllTags() => LoadTagStore().GetAllTags();

    /// <summary>
    /// Removes every indexed chunk for <paramref name="sourcePath"/> in place (without
    /// rebuilding the whole index). Returns the number of chunks removed (0 if the path wasn't
    /// indexed).
    /// </summary>
    public int RemoveDocument(string sourcePath)
    {
        if (!IndexExists()) return 0;

        var chunkCount = GetChunksByPath(sourcePath).Count;
        if (chunkCount == 0) return 0;

        // Open an owned writer (always needs its own FSDirectory with write lock).
        using var directory = FSDirectory.Open(_indexPath);
        using var analyzer = new StandardAnalyzer(DocumentIndexer.MatchVersion);
        var config = new IndexWriterConfig(DocumentIndexer.MatchVersion, analyzer)
        {
            OpenMode = OpenMode.APPEND
        };

        using var writer = new IndexWriter(directory, config);
        writer.DeleteDocuments(new Term("path", sourcePath));
        writer.Commit();

        // The shared reader in LuceneIndexManager will pick up the deletion via
        // DirectoryReader.OpenIfChanged on the next AcquireReader call.

        return chunkCount;
    }

    public IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5, SearchFilters? filters = null)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !IndexExists())
        {
            return Array.Empty<SearchResultItem>();
        }

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<SearchResultItem>();

        using var analyzer = new StandardAnalyzer(DocumentIndexer.MatchVersion);
        var searcher = new IndexSearcher(handle.Reader);
        var parser = new QueryParser(DocumentIndexer.MatchVersion, "content", analyzer)
        {
            DefaultOperator = Operator.OR
        };

        var contentQuery = parser.Parse(QueryParserBase.Escape(queryText));
        var query = ApplyFilters(contentQuery, filters);

        var hasTagFilter = !string.IsNullOrWhiteSpace(filters?.Tag);
        var fetchCount = hasTagFilter
            ? Math.Min(topK * TagFilterCandidateMultiplier, Math.Max(handle.Reader.MaxDoc, 1))
            : topK;

        var hits = searcher.Search(query, fetchCount);

        var highlighter = new Highlighter(new SimpleHTMLFormatter("", ""), new QueryScorer(contentQuery))
        {
            TextFragmenter = new SimpleFragmenter(150)
        };

        var results = new List<SearchResultItem>(hits.ScoreDocs.Length);
        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var content = doc.Get("content") ?? string.Empty;

            string? highlight = null;
            try
            {
                var tokenStream = analyzer.GetTokenStream("content", content);
                highlight = highlighter.GetBestFragment(tokenStream, content);
            }
            catch
            {
                // Highlighting is a nice-to-have; fall back to no highlight rather than
                // failing the whole search if the highlighter can't process this content.
            }

            results.Add(new SearchResultItem
            {
                SourcePath = doc.Get("path") ?? string.Empty,
                FileName = doc.Get("fileName") ?? string.Empty,
                ChunkIndex = int.TryParse(doc.Get("chunkIndex"), out var chunkIndex) ? chunkIndex : 0,
                ChunkText = content,
                Score = scoreDoc.Score,
                Highlight = highlight
            });
        }

        if (hasTagFilter)
        {
            var tagStore = LoadTagStore();
            return results
                .Where(r => tagStore.GetTags(r.SourcePath).Contains(filters!.Tag!, StringComparer.OrdinalIgnoreCase))
                .Take(topK)
                .ToList();
        }

        return results;
    }

    /// <summary>
    /// Busca imagens indexadas visualmente similares a <paramref name="imagePath"/>, gerando um
    /// embedding CLIP da imagem de consulta e comparando por similaridade de cosseno contra os
    /// embeddings armazenados em <c>image_embeddings</c> (ver <see cref="DocumentIndexer"/>/
    /// <see cref="QuestResume.Core.Embeddings.VectorStore"/>).
    /// </summary>
    /// <exception cref="QuestResume.Core.Embeddings.ClipNotConfiguredException">
    /// Quando nenhum modelo CLIP ONNX válido foi configurado (<see cref="Configuration.AppOptions.ClipModelPath"/>).
    /// Comportamento esperado sem um modelo real fornecido pelo usuário.
    /// </exception>
    public async Task<IReadOnlyList<ImageSearchResultItem>> SearchByImageAsync(string imagePath, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (_clipService is null)
        {
            throw new QuestResume.Core.Embeddings.ClipNotConfiguredException(string.Empty);
        }

        var queryEmbedding = await _clipService.EmbedImageAsync(imagePath, cancellationToken).ConfigureAwait(false);

        if (_vectorStore is null)
        {
            return Array.Empty<ImageSearchResultItem>();
        }

        return _vectorStore.SearchImages(queryEmbedding, topK);
    }

    private static Query ApplyFilters(Query contentQuery, SearchFilters? filters)
    {
        if (filters is null || !filters.HasAny)
        {
            return contentQuery;
        }

        var booleanQuery = new BooleanQuery { { contentQuery, Occur.MUST } };

        if (!string.IsNullOrWhiteSpace(filters.Extension))
        {
            var extension = filters.Extension.StartsWith('.') ? filters.Extension : $".{filters.Extension}";
            booleanQuery.Add(new TermQuery(new Term("extension", extension.ToLowerInvariant())), Occur.MUST);
        }

        if (!string.IsNullOrWhiteSpace(filters.FolderPath))
        {
            booleanQuery.Add(new PrefixQuery(new Term("path", filters.FolderPath)), Occur.MUST);
        }

        return booleanQuery;
    }
}
