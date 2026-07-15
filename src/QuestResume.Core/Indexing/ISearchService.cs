using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Read/write façade over a Lucene full-text index. Abstracts <see cref="SearchService"/> so
/// callers (HybridSearchService, RagQueryEngine, API endpoints) are not coupled to Lucene.NET
/// and can be tested with in-memory fakes.
/// </summary>
public interface ISearchService
{
    bool IndexExists();
    int GetDocumentCount();

    /// <summary>Returns all indexed chunks for <paramref name="path"/>, ordered by chunk index.</summary>
    IReadOnlyList<SearchResultItem> GetChunksByPath(string path);

    /// <summary>
    /// Lists every indexed source file with its chunk count and tags.
    /// </summary>
    /// <param name="skip">Number of files to skip (for pagination).</param>
    /// <param name="take">Maximum number of files to return; 0 = unlimited.</param>
    IReadOnlyList<IndexedFileInfo> GetIndexedFiles(int skip = 0, int take = 0);

    IReadOnlyList<string> GetTags(string sourcePath);
    void SetTags(string sourcePath, IEnumerable<string> tags);
    IReadOnlyList<string> GetAllTags();

    /// <summary>Removes all indexed chunks for <paramref name="sourcePath"/>. Returns chunk count removed.</summary>
    int RemoveDocument(string sourcePath);

    IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5, SearchFilters? filters = null);

    /// <summary>
    /// Groups indexed documents by topic via simple k-means over each document's average chunk
    /// embedding. Requires embeddings to be enabled/available. See
    /// <see cref="SearchService.ClusterDocumentsAsync"/> for the algorithm and label-generation details.
    /// </summary>
    Task<IReadOnlyList<Models.DocumentCluster>> ClusterDocumentsAsync(
        int? k = null, Rag.ILlmProvider? llmProvider = null, CancellationToken cancellationToken = default);
}
