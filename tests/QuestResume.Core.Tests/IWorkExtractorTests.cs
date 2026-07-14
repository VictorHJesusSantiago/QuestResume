using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class IWorkExtractorTests
{
    [Theory]
    [InlineData(".pages")]
    [InlineData(".numbers")]
    [InlineData(".key")]
    public void SupportedExtensions_ContainsAllIWorkFormats(string extension)
    {
        var extractor = new IWorkExtractor();
        Assert.Contains(extension, extractor.SupportedExtensions);
    }

    [Fact]
    public async Task ExtractAsync_NoPreviewPdf_ReturnsPartialSupportWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pages");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Index/Document.iwa");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("binary-ish placeholder, not real IWA content");
            }

            var extractor = new IWorkExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
            Assert.Contains("Suporte parcial", document.Metadata["warning"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_NotAValidZip_ReturnsWarningInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.key");

        try
        {
            await File.WriteAllTextAsync(path, "isto não é um arquivo zip válido");

            var extractor = new IWorkExtractor();
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
