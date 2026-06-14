using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

public class CrossEncoderServiceTests
{
    [Fact]
    public async Task ScoreAsync_NotConfigured_ThrowsRerankingNotConfiguredException()
    {
        using var service = new CrossEncoderService(modelPath: string.Empty, vocabPath: string.Empty);

        await Assert.ThrowsAsync<RerankingNotConfiguredException>(() => service.ScoreAsync("pergunta", "trecho"));
    }

    [Fact]
    public async Task ScoreAsync_PathsPointToMissingFiles_ThrowsRerankingNotConfiguredException()
    {
        var missingModelPath = Path.Combine(Path.GetTempPath(), $"reranker-{Guid.NewGuid()}.onnx");
        var missingVocabPath = Path.Combine(Path.GetTempPath(), $"vocab-{Guid.NewGuid()}.txt");

        using var service = new CrossEncoderService(missingModelPath, missingVocabPath);

        await Assert.ThrowsAsync<RerankingNotConfiguredException>(() => service.ScoreAsync("pergunta", "trecho"));
    }
}
