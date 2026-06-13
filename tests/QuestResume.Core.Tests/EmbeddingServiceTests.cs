using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

public class EmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_NotConfigured_ThrowsEmbeddingsNotConfiguredException()
    {
        using var service = new EmbeddingService(modelPath: string.Empty, vocabPath: string.Empty);

        await Assert.ThrowsAsync<EmbeddingsNotConfiguredException>(() => service.EmbedAsync("texto de teste"));
    }

    [Fact]
    public async Task EmbedAsync_PathsPointToMissingFiles_ThrowsEmbeddingsNotConfiguredException()
    {
        var missingModelPath = Path.Combine(Path.GetTempPath(), $"model-{Guid.NewGuid()}.onnx");
        var missingVocabPath = Path.Combine(Path.GetTempPath(), $"vocab-{Guid.NewGuid()}.txt");

        using var service = new EmbeddingService(missingModelPath, missingVocabPath);

        await Assert.ThrowsAsync<EmbeddingsNotConfiguredException>(() => service.EmbedAsync("texto de teste"));
    }
}
