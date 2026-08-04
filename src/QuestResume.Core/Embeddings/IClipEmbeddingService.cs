namespace QuestResume.Core.Embeddings;

public interface IClipEmbeddingService : IDisposable
{
        Task<float[]> EmbedImageAsync(string imagePath, CancellationToken cancellationToken = default);
}
