using QuestResume.Core.Models;

namespace QuestResume.Core.Embeddings;

public interface IVectorStore : IDisposable
{
        void Add(string sourcePath, string fileName, int chunkIndex, string text, float[] embedding);

        IReadOnlyList<SearchResultItem> Search(float[] queryEmbedding, int topK);

        void RemoveBySourcePath(string sourcePath);

        void Clear();

        void InvalidateCache();

        IDisposable BeginBatch();

        IReadOnlyList<(SearchResultItem Item, float[] Embedding)> GetAllEntries();

    

        void AddImageEmbedding(string sourcePath, string fileName, float[] embedding);

        void RemoveImageEmbedding(string sourcePath);

        IReadOnlyList<ImageSearchResultItem> SearchImages(float[] queryEmbedding, int topK);
}
