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
public sealed record SearchFilters(string? Extension = null, string? FolderPath = null)
{
    public bool HasAny => !string.IsNullOrWhiteSpace(Extension) || !string.IsNullOrWhiteSpace(FolderPath);
}

/// <summary>
/// Runs BM25 full-text queries against the Lucene.NET index produced by
/// <see cref="DocumentIndexer"/>.
/// </summary>
public sealed class SearchService
{
    private readonly string _indexPath;

    public SearchService(string indexPath)
    {
        _indexPath = indexPath;
    }

    public bool IndexExists()
    {
        if (!IODirectory.Exists(_indexPath))
        {
            return false;
        }

        using var directory = FSDirectory.Open(_indexPath);
        return DirectoryReader.IndexExists(directory);
    }

    public int GetDocumentCount()
    {
        if (!IndexExists())
        {
            return 0;
        }

        using var directory = FSDirectory.Open(_indexPath);
        using var reader = DirectoryReader.Open(directory);
        return reader.NumDocs;
    }

    /// <summary>
    /// Returns every indexed chunk for the given source file path, ordered by chunk index.
    /// Used by <see cref="QuestResume.Core.Rag.RagQueryEngine.CompareAsync"/> to retrieve a
    /// whole document's content rather than just the chunks matching a query.
    /// </summary>
    public IReadOnlyList<SearchResultItem> GetChunksByPath(string path)
    {
        if (!IndexExists())
        {
            return Array.Empty<SearchResultItem>();
        }

        using var directory = FSDirectory.Open(_indexPath);
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);

        var query = new TermQuery(new Term("path", path));
        var hits = searcher.Search(query, Math.Max(reader.MaxDoc, 1));

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

    public IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5, SearchFilters? filters = null)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !IndexExists())
        {
            return Array.Empty<SearchResultItem>();
        }

        using var directory = FSDirectory.Open(_indexPath);
        using var reader = DirectoryReader.Open(directory);
        using var analyzer = new StandardAnalyzer(DocumentIndexer.MatchVersion);

        var searcher = new IndexSearcher(reader);
        var parser = new QueryParser(DocumentIndexer.MatchVersion, "content", analyzer)
        {
            DefaultOperator = Operator.OR
        };

        var contentQuery = parser.Parse(QueryParserBase.Escape(queryText));
        var query = ApplyFilters(contentQuery, filters);

        var hits = searcher.Search(query, topK);

        var highlighter = new Highlighter(new SimpleHTMLFormatter("", ""), new QueryScorer(contentQuery))
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

        return results;
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
