using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

/// <summary>
/// Minimal <see cref="IEmbeddingService"/> stub for tests that exercise embedding-dependent
/// features (semantic chunking, contextual retrieval, semantic deduplication) without loading a
/// real ONNX model. Produces a small deterministic vector counting how many times each of
/// <paramref name="keywords"/> occurs in the input text (case-insensitive), so texts about the
/// same "topic" (sharing a keyword) score highly similar by cosine similarity, and texts about
/// different topics score near-orthogonal — enough signal for semantic-chunking/dedup logic to
/// behave meaningfully in tests.
/// </summary>
public sealed class FakeEmbeddingService(IReadOnlyList<string> keywords) : IEmbeddingService
{
    public List<string> EmbeddedTexts { get; } = new();

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        EmbeddedTexts.Add(text);

        var lower = text.ToLowerInvariant();
        var vector = new float[keywords.Count + 1];

        for (var i = 0; i < keywords.Count; i++)
        {
            var count = 0;
            var index = 0;
            while ((index = lower.IndexOf(keywords[i], index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += keywords[i].Length;
            }
            vector[i] = count;
        }

        // Constant last dimension avoids an all-zero vector (which EmbeddingMath.CosineSimilarity
        // treats as 0 similarity to everything) for texts that mention none of the keywords.
        vector[^1] = 1f;

        return Task.FromResult(vector);
    }

    public void Dispose()
    {
    }
}
