using Lucene.Net.Analysis.Br;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Search.Spell;
using Lucene.Net.Store;
using Lucene.Net.Util;
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
/// <param name="Fuzzy">
/// When true, each term of <c>queryText</c> is matched with Lucene's <c>FuzzyQuery</c>
/// (tolerant to typos) instead of an exact term match. Ignored for explicit phrase queries
/// (<c>"..."</c>) and boolean/field syntax typed by the user — see
/// <see cref="SearchService.Search"/> for how raw query syntax is detected.
/// </param>
/// <param name="DateFrom">Restricts results to chunks whose file <c>ModifiedUtc</c> is on/after this date (inclusive).</param>
/// <param name="DateTo">Restricts results to chunks whose file <c>ModifiedUtc</c> is on/before this date (inclusive).</param>
/// <param name="MinSizeBytes">Restricts results to files whose size in bytes is at least this value.</param>
/// <param name="MaxSizeBytes">Restricts results to files whose size in bytes is at most this value.</param>
/// <param name="SortBy">
/// <c>"relevance"</c> (default, BM25 score order), <c>"date"</c> (most recently modified
/// file first) or <c>"name"</c> (file name, alphabetical).
/// </param>
/// <param name="SortDescending">Reverses the sort order for <c>"date"</c>/<c>"name"</c>; ignored for <c>"relevance"</c>.</param>
/// <param name="Page">1-based page number for pagination (see <see cref="SearchService.Search"/> for the pagination approach chosen). 0/negative = page 1.</param>
/// <param name="PageSize">Results per page; 0 = no pagination (return up to <c>topK</c> as before).</param>
public sealed record SearchFilters(
    string? Extension = null,
    string? FolderPath = null,
    string? Tag = null,
    bool Fuzzy = false,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    long? MinSizeBytes = null,
    long? MaxSizeBytes = null,
    string SortBy = "relevance",
    bool SortDescending = true,
    int Page = 1,
    int PageSize = 0,
    string? Language = null)
{
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Extension) || !string.IsNullOrWhiteSpace(FolderPath) || !string.IsNullOrWhiteSpace(Tag)
        || DateFrom.HasValue || DateTo.HasValue || MinSizeBytes.HasValue || MaxSizeBytes.HasValue
        || !string.IsNullOrWhiteSpace(Language);
}

/// <summary>Thrown when a raw Lucene query string typed by the user has invalid syntax (e.g. unbalanced quotes).</summary>
public sealed class SearchQuerySyntaxException : Exception
{
    public SearchQuerySyntaxException(string message) : base(message)
    {
    }
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
                Score = scoreDoc.Score,
                PageNumber = int.TryParse(doc.Get("pageNumber"), out var pageNumber) ? pageNumber : null
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
                    Summary = summaryStore.GetSummary(path),
                    Language = doc.Get("language")
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
        using var analyzer = new BrazilianAnalyzer(DocumentIndexer.MatchVersion);
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

    /// <summary>
    /// Syntax characters that, when present in the raw query, signal the user is deliberately
    /// using Lucene query syntax (phrase, boolean operators, field queries, wildcards) rather
    /// than typing plain keywords. In that case the query is parsed as-is (unescaped) so that
    /// syntax works; otherwise every special character is escaped so accidental syntax
    /// characters in plain text (e.g. a question mark) don't throw <see cref="ParseException"/>.
    /// </summary>
    private static readonly char[] LuceneSyntaxChars = { '"', '+', '-', '(', ')', ':', '*', '~', '^' };

    private static bool LooksLikeQuerySyntax(string queryText)
    {
        if (queryText.IndexOfAny(LuceneSyntaxChars) >= 0) return true;
        return queryText.Contains(" AND ", StringComparison.Ordinal)
            || queryText.Contains(" OR ", StringComparison.Ordinal)
            || queryText.Contains(" NOT ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Appends <c>~</c> (Lucene fuzzy-match operator) to each plain-text term of
    /// <paramref name="queryText"/>, used for <see cref="SearchFilters.Fuzzy"/>. Applying fuzzy
    /// matching to raw syntax queries (phrases, field queries) rarely does what the user
    /// intends, so this is only called when <see cref="LooksLikeQuerySyntax"/> is false.
    /// </summary>
    private static string MakeFuzzy(string queryText) =>
        string.Join(' ', queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(term => $"{term}~"));

    /// <summary>
    /// Runs a BM25 full-text query. Supports Lucene query syntax typed deliberately by the
    /// user — exact phrases (<c>"frase exata"</c>) and boolean operators
    /// (<c>AND</c>/<c>OR</c>/<c>NOT</c>/<c>-termo</c>/field queries) — by parsing the raw text
    /// as-is instead of escaping it, whenever <see cref="LooksLikeQuerySyntax"/> detects such
    /// syntax; plain keyword queries are still escaped so incidental punctuation can't break
    /// parsing. Invalid user syntax (e.g. unbalanced quotes) is caught and surfaced as
    /// <see cref="SearchQuerySyntaxException"/> instead of propagating a raw Lucene
    /// <see cref="ParseException"/>.
    /// </summary>
    /// <exception cref="SearchQuerySyntaxException">When <paramref name="queryText"/> contains invalid Lucene query syntax.</exception>
    public IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5, SearchFilters? filters = null)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !IndexExists())
        {
            return Array.Empty<SearchResultItem>();
        }

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<SearchResultItem>();

        using var analyzer = new BrazilianAnalyzer(DocumentIndexer.MatchVersion);
        var searcher = new IndexSearcher(handle.Reader);
        var parser = new QueryParser(DocumentIndexer.MatchVersion, "content", analyzer)
        {
            DefaultOperator = Operator.OR
        };

        var isRawSyntax = LooksLikeQuerySyntax(queryText);
        var parseInput = isRawSyntax
            ? queryText
            : (filters?.Fuzzy ?? false) ? MakeFuzzy(queryText) : QueryParserBase.Escape(queryText);

        Query contentQuery;
        try
        {
            contentQuery = parser.Parse(parseInput);
        }
        catch (ParseException ex)
        {
            throw new SearchQuerySyntaxException($"Sintaxe de busca inválida: {ex.Message}");
        }

        var query = ApplyFilters(contentQuery, filters);

        var hasTagFilter = !string.IsNullOrWhiteSpace(filters?.Tag);
        var page = filters is { PageSize: > 0 } ? Math.Max(1, filters.Page) : 1;
        var pageSize = filters?.PageSize ?? 0;

        // Pagination approach: fetch (page * pageSize) — or topK when unpaginated — candidates
        // from Lucene and slice the requested page in memory, rather than IndexSearcher.SearchAfter.
        // Simpler to reason about alongside the existing tag post-filter and score/date/name
        // sort variants, and acceptable for the moderate per-collection document counts this
        // desktop-oriented tool targets; SearchAfter would be worth revisiting if collections grow
        // into the millions of chunks.
        var effectiveTopK = pageSize > 0 ? page * pageSize : topK;
        var fetchCount = hasTagFilter
            ? Math.Min(effectiveTopK * TagFilterCandidateMultiplier, Math.Max(handle.Reader.MaxDoc, 1))
            : Math.Min(Math.Max(effectiveTopK, 1), Math.Max(handle.Reader.MaxDoc, 1));

        var sort = BuildSort(filters);
        var hits = sort is null ? searcher.Search(query, fetchCount) : searcher.Search(query, fetchCount, sort);

        var highlighter = new Highlighter(new SimpleHTMLFormatter("", ""), new QueryScorer(contentQuery))
        {
            TextFragmenter = new SimpleFragmenter(150)
        };

        var results = new List<SearchResultItem>(hits.ScoreDocs.Length);
        foreach (var scoreDoc in hits.ScoreDocs)
        {
            var doc = searcher.Doc(scoreDoc.Doc);
            var content = doc.Get("content") ?? string.Empty;

            // Parent-child chunking (AppOptions.ParentChildChunkingEnabled): matching happens on
            // the small "content" (child) field above, but the text returned/used in the LLM
            // prompt should be the larger parent chunk — see DocumentIndexer.ToLuceneDocument.
            var parentText = doc.Get("parentText");
            var displayText = !string.IsNullOrEmpty(parentText) ? parentText : content;

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
                ChunkText = displayText,
                Score = scoreDoc.Score,
                Highlight = highlight,
                PageNumber = int.TryParse(doc.Get("pageNumber"), out var pageNumber) ? pageNumber : null,
                ModifiedUtc = long.TryParse(doc.Get("modifiedUtc"), out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : default,
                SizeBytes = long.TryParse(doc.Get("sizeBytes"), out var size) ? size : 0
            });
        }

        IEnumerable<SearchResultItem> final = results;

        if (hasTagFilter)
        {
            var tagStore = LoadTagStore();
            final = final.Where(r => tagStore.GetTags(r.SourcePath).Contains(filters!.Tag!, StringComparer.OrdinalIgnoreCase));
        }

        if (pageSize > 0)
        {
            final = final.Skip((page - 1) * pageSize).Take(pageSize);
        }
        else
        {
            final = final.Take(topK);
        }

        return final.ToList();
    }

    /// <summary>
    /// Builds a Lucene <see cref="Sort"/> for <see cref="SearchFilters.SortBy"/>, or <c>null</c>
    /// to keep the default relevance (BM25 score) order.
    /// </summary>
    private static Sort? BuildSort(SearchFilters? filters)
    {
        if (filters is null) return null;

        return filters.SortBy?.ToLowerInvariant() switch
        {
            "date" => new Sort(new SortField("modifiedUtcTicks", SortFieldType.INT64, reverse: filters.SortDescending)),
            "name" => new Sort(new SortField("fileName", SortFieldType.STRING, reverse: filters.SortDescending)),
            _ => null
        };
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

    /// <summary>
    /// Sentence-window retrieval (<see cref="Configuration.AppOptions.SentenceWindowChunkingEnabled"/>):
    /// given search results whose chunks are individual sentences (see
    /// <see cref="TextChunker.ChunkBySentences"/>), replaces each result's <see cref="SearchResultItem.ChunkText"/>
    /// with that sentence plus <paramref name="windowSize"/> sibling sentences before/after,
    /// reconstructed from the other chunks of the same source document (already indexed
    /// contiguously by <see cref="TextChunk.ChunkIndex"/>). Falls back to the original chunk
    /// text if the source document's chunks can't be found.
    /// </summary>
    public IReadOnlyList<SearchResultItem> ExpandSentenceWindow(IReadOnlyList<SearchResultItem> results, int windowSize)
    {
        if (windowSize <= 0 || results.Count == 0) return results;

        var byPath = new Dictionary<string, IReadOnlyList<SearchResultItem>>();
        var expanded = new List<SearchResultItem>(results.Count);

        foreach (var result in results)
        {
            if (!byPath.TryGetValue(result.SourcePath, out var siblings))
            {
                siblings = GetChunksByPath(result.SourcePath);
                byPath[result.SourcePath] = siblings;
            }

            if (siblings.Count == 0)
            {
                expanded.Add(result);
                continue;
            }

            var windowText = string.Join(' ', siblings
                .Where(s => s.ChunkIndex >= result.ChunkIndex - windowSize && s.ChunkIndex <= result.ChunkIndex + windowSize)
                .OrderBy(s => s.ChunkIndex)
                .Select(s => s.ChunkText));

            expanded.Add(new SearchResultItem
            {
                SourcePath = result.SourcePath,
                FileName = result.FileName,
                ChunkIndex = result.ChunkIndex,
                ChunkText = windowText.Length > 0 ? windowText : result.ChunkText,
                Score = result.Score,
                Highlight = result.Highlight,
                PageNumber = result.PageNumber,
                ModifiedUtc = result.ModifiedUtc,
                SizeBytes = result.SizeBytes
            });
        }

        return expanded;
    }

    /// <summary>
    /// Corretor ortográfico (item 11): for each whitespace-separated term of
    /// <paramref name="queryText"/>, asks Lucene's <see cref="DirectSpellChecker"/> — which
    /// compares directly against the live index's term dictionary, so no separate spellcheck
    /// index needs to be built/maintained, unlike the classic <c>Lucene.Net.Search.Spell.SpellChecker</c>
    /// this item names — for the closest indexed terms and returns the flattened, de-duplicated
    /// list of suggestions. Intended to be called by API/CLI callers when a search returns no or
    /// few results, exposed as <c>didYouMean</c>.
    /// Note: suggestions are compared against already-analyzed (lowercased/stemmed by
    /// <see cref="BrazilianAnalyzer"/>) indexed terms, so a suggestion may be a word stem rather
    /// than a full natural-language word — an accepted limitation of spellchecking against an
    /// analyzed field instead of a dedicated unanalyzed "raw terms" field.
    /// </summary>
    public IReadOnlyList<string> SuggestSpelling(string queryText, int maxSuggestionsPerTerm = 3)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !IndexExists()) return Array.Empty<string>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<string>();

        try
        {
            // Lower than the Lucene default (0.5f): indexed terms are post-stemming (see the
            // BrazilianAnalyzer applied at index time), so a raw typo can differ from its
            // stemmed match more than the default accuracy threshold tolerates.
            var spellChecker = new DirectSpellChecker { Accuracy = 0.3f };
            var suggestions = new List<string>();

            foreach (var rawTerm in queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = new string(rawTerm.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
                if (cleaned.Length == 0) continue;

                var suggestWords = spellChecker.SuggestSimilar(new Term("content", cleaned), maxSuggestionsPerTerm, handle.Reader);
                suggestions.AddRange(suggestWords.Select(s => s.String));
            }

            return suggestions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch
        {
            // Best-effort: spellcheck is a secondary feature of the search endpoint — a Lucene
            // failure here shouldn't be surfaced as a search error.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Autocomplete/sugestões (item 12): returns up to <paramref name="maxSuggestions"/> indexed
    /// terms starting with <paramref name="prefix"/> (case-insensitive), ordered by document
    /// frequency (most common first). Built directly from the live index's term dictionary via
    /// <see cref="TermsEnum.SeekCeil(BytesRef)"/> — a lighter-weight alternative to building and
    /// persisting a separate <c>Lucene.Net.Search.Suggest.Analyzing.AnalyzingSuggester</c>
    /// structure, at the cost of not supporting multi-word/whole-phrase suggestions (only single
    /// indexed terms). Used by <c>GET /api/search/suggest</c>.
    /// </summary>
    public IReadOnlyList<string> Suggest(string prefix, int maxSuggestions = 10)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !IndexExists()) return Array.Empty<string>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<string>();

        try
        {
            var lowerPrefix = prefix.Trim().ToLowerInvariant();
            var terms = MultiFields.GetTerms(handle.Reader, "content");
            if (terms is null) return Array.Empty<string>();

            var termsEnum = terms.GetEnumerator();
            var results = new List<(string Term, int DocFreq)>();
            var prefixBytes = new BytesRef(lowerPrefix);

            // Cap how many terms we scan past the prefix so a very common prefix (e.g. a single
            // letter) can't turn an autocomplete request into a full term-dictionary scan.
            const int maxTermsScanned = 500;
            var scanned = 0;

            if (termsEnum.SeekCeil(prefixBytes) != TermsEnum.SeekStatus.END)
            {
                do
                {
                    var termText = termsEnum.Term.Utf8ToString();
                    if (!termText.StartsWith(lowerPrefix, StringComparison.Ordinal)) break;

                    results.Add((termText, termsEnum.DocFreq));
                    scanned++;
                } while (scanned < maxTermsScanned && termsEnum.MoveNext());
            }

            return results
                .OrderByDescending(r => r.DocFreq)
                .ThenBy(r => r.Term, StringComparer.OrdinalIgnoreCase)
                .Take(maxSuggestions)
                .Select(r => r.Term)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// "Mais como este" (item 17): given an already-indexed document, computes the average
    /// embedding of its chunks and returns other documents ranked by cosine similarity between
    /// average embeddings, excluding the reference document itself.
    /// </summary>
    /// <exception cref="QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException">
    /// When embeddings aren't configured (no <see cref="QuestResume.Core.Embeddings.IVectorStore"/> available).
    /// </exception>
    public Task<IReadOnlyList<SimilarDocumentResult>> FindSimilarAsync(string sourcePath, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (_vectorStore is null)
        {
            throw new QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException(string.Empty, string.Empty);
        }

        var entries = _vectorStore.GetAllEntries();
        var referenceEmbeddings = entries.Where(e => e.Item.SourcePath == sourcePath).Select(e => e.Embedding).ToList();

        if (referenceEmbeddings.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<SimilarDocumentResult>>(Array.Empty<SimilarDocumentResult>());
        }

        var referenceAverage = QuestResume.Core.Embeddings.EmbeddingMath.Average(referenceEmbeddings);

        var results = entries
            .Where(e => e.Item.SourcePath != sourcePath)
            .GroupBy(e => e.Item.SourcePath)
            .Select(g => new SimilarDocumentResult
            {
                SourcePath = g.Key,
                FileName = g.First().Item.FileName,
                Similarity = QuestResume.Core.Embeddings.EmbeddingMath.CosineSimilarity(
                    referenceAverage, QuestResume.Core.Embeddings.EmbeddingMath.Average(g.Select(e => e.Embedding).ToList()))
            })
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .ToList();

        return Task.FromResult<IReadOnlyList<SimilarDocumentResult>>(results);
    }

    /// <summary>
    /// Clustering automático de documentos por tema (item 1): computes the average chunk
    /// embedding of every indexed document and groups them with a simple k-means (Lloyd's
    /// algorithm, Euclidean distance, k-means++-style seeding) into <paramref name="k"/> clusters.
    /// When <paramref name="k"/> is omitted, uses the heuristic <c>ceil(sqrt(N / 2))</c> (at least
    /// 1, capped at document count) commonly used for a first pass at an unknown number of topics.
    /// When <paramref name="llmProvider"/> is supplied, asks it for a short label summarizing each
    /// cluster's apparent topic from a sample of its documents' text — best-effort, a label stays
    /// <c>null</c> if no provider is given or generation fails.
    /// </summary>
    /// <exception cref="QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException">
    /// When embeddings aren't configured (no <see cref="QuestResume.Core.Embeddings.IVectorStore"/> available).
    /// </exception>
    public async Task<IReadOnlyList<DocumentCluster>> ClusterDocumentsAsync(
        int? k = null, Rag.ILlmProvider? llmProvider = null, CancellationToken cancellationToken = default)
    {
        if (_vectorStore is null)
        {
            throw new QuestResume.Core.Embeddings.EmbeddingsNotConfiguredException(string.Empty, string.Empty);
        }

        var entries = _vectorStore.GetAllEntries();
        if (entries.Count == 0)
        {
            return Array.Empty<DocumentCluster>();
        }

        var averageByPath = entries
            .GroupBy(e => e.Item.SourcePath)
            .ToDictionary(g => g.Key, g => QuestResume.Core.Embeddings.EmbeddingMath.Average(g.Select(e => e.Embedding).ToList()));

        var paths = averageByPath.Keys.ToList();
        if (paths.Count == 1)
        {
            var singleLabel = llmProvider is not null
                ? await TryGenerateClusterLabelAsync(llmProvider, new[] { paths[0] }, entries, cancellationToken).ConfigureAwait(false)
                : null;

            return new List<DocumentCluster>
            {
                new() { ClusterId = 0, SourcePaths = paths, Label = singleLabel }
            };
        }

        var effectiveK = k.HasValue && k.Value > 0
            ? Math.Min(k.Value, paths.Count)
            : Math.Max(1, (int)Math.Ceiling(Math.Sqrt(paths.Count / 2.0)));

        var assignments = KMeans(paths.Select(p => averageByPath[p]).ToList(), effectiveK);

        var clusters = new List<DocumentCluster>();
        for (var clusterId = 0; clusterId < effectiveK; clusterId++)
        {
            var clusterPaths = paths.Where((_, i) => assignments[i] == clusterId).ToList();
            if (clusterPaths.Count == 0) continue;

            string? label = null;
            if (llmProvider is not null)
            {
                label = await TryGenerateClusterLabelAsync(llmProvider, clusterPaths, entries, cancellationToken).ConfigureAwait(false);
            }

            clusters.Add(new DocumentCluster
            {
                ClusterId = clusterId,
                SourcePaths = clusterPaths,
                Label = label
            });
        }

        return clusters;
    }

    /// <summary>
    /// Simple Lloyd's-algorithm k-means over Euclidean distance, with k-means++-style seeding
    /// (each successive centroid chosen with probability proportional to its squared distance
    /// from the nearest already-chosen centroid) for more stable results than pure random
    /// initialization. Returns the cluster index (0..k-1) assigned to each input vector, in order.
    /// </summary>
    private static int[] KMeans(IReadOnlyList<float[]> vectors, int k, int maxIterations = 50)
    {
        var n = vectors.Count;
        k = Math.Max(1, Math.Min(k, n));

        var random = new Random(42); // deterministic seeding keeps repeated calls stable
        var centroids = new List<float[]>(k);
        centroids.Add(vectors[random.Next(n)]);

        while (centroids.Count < k)
        {
            var distances = vectors.Select(v => centroids.Min(c => SquaredDistance(v, c))).ToArray();
            var total = distances.Sum();
            if (total <= 0)
            {
                // All remaining points coincide with an existing centroid; pick any point left.
                centroids.Add(vectors[random.Next(n)]);
                continue;
            }

            var target = random.NextDouble() * total;
            var cumulative = 0.0;
            var chosenIndex = n - 1;
            for (var i = 0; i < n; i++)
            {
                cumulative += distances[i];
                if (cumulative >= target)
                {
                    chosenIndex = i;
                    break;
                }
            }
            centroids.Add(vectors[chosenIndex]);
        }

        var assignments = new int[n];

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;

            for (var i = 0; i < n; i++)
            {
                var bestCluster = 0;
                var bestDistance = double.MaxValue;
                for (var c = 0; c < k; c++)
                {
                    var distance = SquaredDistance(vectors[i], centroids[c]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCluster = c;
                    }
                }

                if (assignments[i] != bestCluster)
                {
                    changed = true;
                    assignments[i] = bestCluster;
                }
            }

            var newCentroids = new float[k][];
            var counts = new int[k];
            var dimension = vectors[0].Length;
            for (var c = 0; c < k; c++)
            {
                newCentroids[c] = new float[dimension];
            }

            for (var i = 0; i < n; i++)
            {
                var cluster = assignments[i];
                counts[cluster]++;
                for (var d = 0; d < dimension; d++)
                {
                    newCentroids[cluster][d] += vectors[i][d];
                }
            }

            for (var c = 0; c < k; c++)
            {
                if (counts[c] == 0)
                {
                    newCentroids[c] = centroids[c];
                    continue;
                }

                for (var d = 0; d < dimension; d++)
                {
                    newCentroids[c][d] /= counts[c];
                }
            }

            centroids = newCentroids.ToList();

            if (!changed)
            {
                break;
            }
        }

        return assignments;
    }

    private static double SquaredDistance(float[] a, float[] b)
    {
        double sum = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var i = 0; i < length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return sum;
    }

    /// <summary>
    /// Best-effort LLM label for a cluster: samples up to 3 documents' first ~500 characters of
    /// chunk text and asks for a short (few words) topic summary. Returns null (never throws) on
    /// any failure, so a broken/unavailable LLM never breaks clustering itself.
    /// </summary>
    private static async Task<string?> TryGenerateClusterLabelAsync(
        Rag.ILlmProvider llmProvider,
        IReadOnlyList<string> clusterPaths,
        IReadOnlyList<(SearchResultItem Item, float[] Embedding)> entries,
        CancellationToken cancellationToken)
    {
        try
        {
            var samples = clusterPaths
                .Take(3)
                .Select(path => entries.FirstOrDefault(e => e.Item.SourcePath == path).Item?.ChunkText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!.Length > 500 ? text[..500] : text)
                .ToList();

            if (samples.Count == 0)
            {
                return null;
            }

            var prompt =
                "Com base nos trechos de documentos abaixo, gere um rótulo curto (no máximo 5 palavras) " +
                "em português que resuma o tema em comum entre eles. Responda apenas com o rótulo, sem explicações.\n\n" +
                string.Join("\n---\n", samples);

            var label = await llmProvider.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            label = label?.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(label) ? null : label;
        }
        catch
        {
            return null;
        }
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

        if (filters.DateFrom.HasValue || filters.DateTo.HasValue)
        {
            var min = filters.DateFrom?.ToUniversalTime().Ticks;
            var max = filters.DateTo?.ToUniversalTime().Ticks;
            booleanQuery.Add(NumericRangeQuery.NewInt64Range("modifiedUtcTicks", min, max, true, true), Occur.MUST);
        }

        if (filters.MinSizeBytes.HasValue || filters.MaxSizeBytes.HasValue)
        {
            booleanQuery.Add(NumericRangeQuery.NewInt64Range("sizeBytesIndexed", filters.MinSizeBytes, filters.MaxSizeBytes, true, true), Occur.MUST);
        }

        if (!string.IsNullOrWhiteSpace(filters.Language))
        {
            booleanQuery.Add(new TermQuery(new Term("language", filters.Language.ToLowerInvariant())), Occur.MUST);
        }

        return booleanQuery;
    }
}
