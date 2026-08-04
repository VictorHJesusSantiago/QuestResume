namespace QuestResume.Core.Embeddings;

public interface IEmbeddingService : IDisposable
{
        Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
