using QuestResume.Core.Embeddings;

namespace QuestResume.Core.Tests;

public class ClipEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedImageAsync_NotConfigured_ThrowsClipNotConfiguredException()
    {
        using var service = new ClipEmbeddingService(modelPath: string.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(), $"img-{Guid.NewGuid()}.png");

        await Assert.ThrowsAsync<ClipNotConfiguredException>(() => service.EmbedImageAsync(imagePath));
    }

    [Fact]
    public async Task EmbedImageAsync_ModelPathPointsToMissingFile_ThrowsClipNotConfiguredException()
    {
        var missingModelPath = Path.Combine(Path.GetTempPath(), $"clip-{Guid.NewGuid()}.onnx");
        using var service = new ClipEmbeddingService(missingModelPath);
        var imagePath = Path.Combine(Path.GetTempPath(), $"img-{Guid.NewGuid()}.png");

        await Assert.ThrowsAsync<ClipNotConfiguredException>(() => service.EmbedImageAsync(imagePath));
    }

    [Fact]
    public async Task SearchByImageAsync_WithoutClipService_ThrowsClipNotConfiguredException()
    {
        var indexPath = Path.Combine(Path.GetTempPath(), $"search-image-{Guid.NewGuid()}");
        Directory.CreateDirectory(indexPath);

        try
        {
            var search = new QuestResume.Core.Indexing.SearchService(indexPath);
            var imagePath = Path.Combine(Path.GetTempPath(), $"img-{Guid.NewGuid()}.png");

            await Assert.ThrowsAsync<ClipNotConfiguredException>(() => search.SearchByImageAsync(imagePath));
        }
        finally
        {
            Directory.Delete(indexPath, recursive: true);
        }
    }
}
