using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ImageOcrExtractorTests
{
    [Fact]
    public async Task ExtractAsync_TessDataPathNotConfigured_ReturnsEmptyTextWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

        try
        {
            await File.WriteAllBytesAsync(path, CreateMinimalPng());

            var extractor = new ImageOcrExtractor(tessDataPath: string.Empty, languages: "por+eng");
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_TessDataPathPointsToMissingFolder_ReturnsEmptyTextWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        var missingTessData = Path.Combine(Path.GetTempPath(), $"tessdata-{Guid.NewGuid()}");

        try
        {
            await File.WriteAllBytesAsync(path, CreateMinimalPng());

            var extractor = new ImageOcrExtractor(tessDataPath: missingTessData, languages: "por+eng");
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SupportedExtensions_IncludeCommonImageFormats()
    {
        var extractor = new ImageOcrExtractor(tessDataPath: string.Empty, languages: "por+eng");

        Assert.Contains(".png", extractor.SupportedExtensions);
        Assert.Contains(".jpg", extractor.SupportedExtensions);
        Assert.Contains(".jpeg", extractor.SupportedExtensions);
        Assert.Contains(".tiff", extractor.SupportedExtensions);
        Assert.Contains(".bmp", extractor.SupportedExtensions);
        Assert.Contains(".gif", extractor.SupportedExtensions);
    }

    /// <summary>1x1 transparent PNG, just enough for <c>Pix.LoadFromMemory</c> not to be reached.</summary>
    private static byte[] CreateMinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x64, 0x60, 0x60, 0x60,
        0x00, 0x00, 0x00, 0x05, 0x00, 0x01, 0x5A, 0x9C,
        0xCA, 0x73, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];
}
