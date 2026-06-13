using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests.Integration;

/// <summary>
/// Opt-in tests that exercise <see cref="EmbeddingService"/> against a real ONNX model and
/// tokenizer. Skipped (pass trivially) unless <c>QUESTRESUME_TEST_EMBEDDING_MODEL</c> and
/// <c>QUESTRESUME_TEST_EMBEDDING_TOKENIZER</c> point to existing files.
/// </summary>
public class EmbeddingServiceIntegrationTests
{
    [Fact]
    public async Task EmbedAsync_WithRealModel_RanksRelatedTextAboveUnrelatedText()
    {
        var modelPath = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_EMBEDDING_MODEL");
        var vocabPath = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_EMBEDDING_TOKENIZER");

        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)
            || string.IsNullOrWhiteSpace(vocabPath) || !File.Exists(vocabPath))
        {
            return;
        }

        using var service = new EmbeddingService(modelPath, vocabPath);

        var reference = await service.EmbedAsync("o gato dorme no sofá da sala");
        var related = await service.EmbedAsync("um gatinho está descansando no sofá");
        var unrelated = await service.EmbedAsync("o servidor reiniciou após a atualização de segurança");

        Assert.NotEmpty(reference);
        Assert.Equal(reference.Length, related.Length);
        Assert.Equal(reference.Length, unrelated.Length);

        var similarityToRelated = CosineSimilarity(reference, related);
        var similarityToUnrelated = CosineSimilarity(reference, unrelated);

        Assert.True(similarityToRelated > similarityToUnrelated,
            $"esperado similaridade com texto relacionado ({similarityToRelated}) maior que com texto não relacionado ({similarityToUnrelated})");
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}
