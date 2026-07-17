using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ChmDjvuMetadataExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ChmFile_ReturnsMetadataOnlyWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.chm");

        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x49, 0x54, 0x53, 0x46, 0x00, 0x00, 0x00, 0x00 });

            var extractor = new ChmDjvuMetadataExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
            Assert.Contains("CHM", document.Metadata["warning"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_DjvuFile_ReturnsMetadataOnlyWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.djvu");

        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x41, 0x54, 0x26, 0x54 });

            var extractor = new ChmDjvuMetadataExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
