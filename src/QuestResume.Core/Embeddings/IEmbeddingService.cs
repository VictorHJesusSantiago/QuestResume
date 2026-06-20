namespace QuestResume.Core.Embeddings;

/// <summary>
/// Generates dense vector embeddings for text using a local ONNX model.
/// Abstracts the concrete <see cref="EmbeddingService"/> so callers (DocumentIndexer,
/// HybridSearchService) are not coupled to ONNX Runtime or a specific tokenizer.
/// </summary>
public interface IEmbeddingService : IDisposable
{
    /// <summary>
    /// Computes a normalized embedding vector for <paramref name="text"/>.
    /// </summary>
    /// <exception cref="EmbeddingsNotConfiguredException">
    /// Thrown if the model or vocabulary path is missing or fails to load.
    /// </exception>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
