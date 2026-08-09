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

public sealed class SearchQuerySyntaxException : Exception
{
    public SearchQuerySyntaxException(string message) : base(message)
    {
    }
}

public sealed class SearchService : ISearchService
{
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

        public Task SealAsync()
    {
        if (_encryptionSalt is not null && !string.IsNullOrEmpty(_masterPassword))
        {
            return LuceneIndexEncryptionService.SealAsync(_indexPath, _indexPath, _masterPassword, _encryptionSalt);
        }

        return Task.CompletedTask;
    }

    
    
    

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

        public IReadOnlyList<IndexedFileInfo> GetIndexedFiles(int skip = 0, int take = 0)
    {
        if (!IndexExists()) return Array.Empty<IndexedFileInfo>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<IndexedFileInfo>();

        var searcher = new IndexSearcher(handle.Reader);
        
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

        public IReadOnlyList<string> GetTags(string sourcePath) => LoadTagStore().GetTags(sourcePath);

        public void SetTags(string sourcePath, IEnumerable<string> tags)
    {
        var store = LoadTagStore();
        store.SetTags(sourcePath, tags);
        SaveTagStoreAndInvalidate(store);
    }

        public IReadOnlyList<string> GetAllTags() => LoadTagStore().GetAllTags();

        public int RemoveDocument(string sourcePath)
    {
        if (!IndexExists()) return 0;

        var chunkCount = GetChunksByPath(sourcePath).Count;
        if (chunkCount == 0) return 0;

        
        using var directory = FSDirectory.Open(_indexPath);
        using var analyzer = new BrazilianAnalyzer(DocumentIndexer.MatchVersion);
        var config = new IndexWriterConfig(DocumentIndexer.MatchVersion, analyzer)
        {
            OpenMode = OpenMode.APPEND
        };

        using var writer = new IndexWriter(directory, config);
        writer.DeleteDocuments(new Term("path", sourcePath));
        writer.Commit();

        
        

        return chunkCount;
    }

        private static readonly char[] LuceneSyntaxChars = { '"', '+', '-', '(', ')', ':', '*', '~', '^' };

    private static bool LooksLikeQuerySyntax(string queryText)
    {
        if (queryText.IndexOfAny(LuceneSyntaxChars) >= 0) return true;
        return queryText.Contains(" AND ", StringComparison.Ordinal)
            || queryText.Contains(" OR ", StringComparison.Ordinal)
            || queryText.Contains(" NOT ", StringComparison.Ordinal);
    }

        private static string MakeFuzzy(string queryText) =>
        string.Join(' ', queryText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(term => $"{term}~"));

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

        public IReadOnlyList<string> SuggestSpelling(string queryText, int maxSuggestionsPerTerm = 3)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !IndexExists()) return Array.Empty<string>();

        using var handle = OpenReader();
        if (handle.Reader is null) return Array.Empty<string>();

        try
        {
            
            
            
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
            
            
            return Array.Empty<string>();
        }
    }

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

        private static int[] KMeans(IReadOnlyList<float[]> vectors, int k, int maxIterations = 50)
    {
        var n = vectors.Count;
        k = Math.Max(1, Math.Min(k, n));

        var random = new Random(42); 
        var centroids = new List<float[]>(k);
        centroids.Add(vectors[random.Next(n)]);

        while (centroids.Count < k)
        {
            var distances = vectors.Select(v => centroids.Min(c => SquaredDistance(v, c))).ToArray();
            var total = distances.Sum();
            if (total <= 0)
            {
                
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
