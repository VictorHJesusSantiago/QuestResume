namespace QuestResume.Core.Embeddings;

/// <summary>
/// Abstrai <see cref="ClipEmbeddingService"/> para permitir substituição em testes sem carregar
/// um modelo ONNX real.
/// </summary>
public interface IClipEmbeddingService : IDisposable
{
    /// <exception cref="ClipNotConfiguredException">Quando o modelo CLIP não está configurado ou falha ao carregar.</exception>
    Task<float[]> EmbedImageAsync(string imagePath, CancellationToken cancellationToken = default);
}
