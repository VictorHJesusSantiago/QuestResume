namespace QuestResume.Core.Embeddings;

/// <summary>
/// Scores relevance of a passage relative to a query using a local ONNX cross-encoder.
/// Abstracts <see cref="CrossEncoderService"/> so <see cref="QuestResume.Core.Indexing.HybridSearchService"/>
/// is not coupled to a specific ONNX model or tokenizer implementation.
/// </summary>
public interface ICrossEncoderService : IDisposable
{
    /// <summary>
    /// Computes a 0-1 relevance score for <paramref name="passage"/> given <paramref name="query"/>.
    /// </summary>
    /// <exception cref="RerankingNotConfiguredException">
    /// Thrown if the re-ranking model or vocabulary path is missing or fails to load.
    /// </exception>
    Task<float> ScoreAsync(string query, string passage, CancellationToken cancellationToken = default);
}
