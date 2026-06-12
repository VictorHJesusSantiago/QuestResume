using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using QuestResume.Core.Models;
using IODirectory = System.IO.Directory;

namespace QuestResume.Core.Indexing;

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

    public IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5)
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

        var query = parser.Parse(QueryParserBase.Escape(queryText));
        var hits = searcher.Search(query, topK);

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

        return results;
    }
}
