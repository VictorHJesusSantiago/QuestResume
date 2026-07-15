namespace QuestResume.Core.Embeddings;

/// <summary>
/// Small vector-math helpers shared by the semantic-quality features of the RAG pipeline
/// (semantic chunking, "mais como este", deduplicação semântica) so the same cosine-similarity
/// definition used by <see cref="VectorStore"/>/<see cref="EncryptedVectorStore"/> for search
/// scoring is reused everywhere embeddings are compared, instead of being redefined per caller.
/// </summary>
public static class EmbeddingMath
{
    /// <summary>Cosine similarity between two equal-length vectors; 0 for empty/mismatched vectors.</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0f || normB == 0f)
        {
            return 0f;
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }

    /// <summary>Element-wise average of a non-empty collection of equal-length vectors.</summary>
    public static float[] Average(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0)
        {
            return Array.Empty<float>();
        }

        var length = vectors[0].Length;
        var sum = new float[length];

        foreach (var vector in vectors)
        {
            for (var i = 0; i < length && i < vector.Length; i++)
            {
                sum[i] += vector[i];
            }
        }

        for (var i = 0; i < length; i++)
        {
            sum[i] /= vectors.Count;
        }

        return sum;
    }
}
