namespace QuestResume.Core.Embeddings;

public static class EmbeddingMath
{
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
