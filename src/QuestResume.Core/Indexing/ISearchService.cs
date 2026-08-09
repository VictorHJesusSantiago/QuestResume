using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

public interface ISearchService
{
    bool IndexExists();
    int GetDocumentCount();

        IReadOnlyList<SearchResultItem> GetChunksByPath(string path);

        IReadOnlyList<IndexedFileInfo> GetIndexedFiles(int skip = 0, int take = 0);

    IReadOnlyList<string> GetTags(string sourcePath);
    void SetTags(string sourcePath, IEnumerable<string> tags);
    IReadOnlyList<string> GetAllTags();

        int RemoveDocument(string sourcePath);

    IReadOnlyList<SearchResultItem> Search(string queryText, int topK = 5, SearchFilters? filters = null);

        Task<IReadOnlyList<Models.DocumentCluster>> ClusterDocumentsAsync(
        int? k = null, Rag.ILlmProvider? llmProvider = null, CancellationToken cancellationToken = default);
}
