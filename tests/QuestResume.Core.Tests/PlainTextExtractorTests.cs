using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class PlainTextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReadsFileContent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");
        const string content = "Linha 1\nLinha 2 com acentuação: ç, ã, é";

        try
        {
            await File.WriteAllTextAsync(path, content);

            var extractor = new PlainTextExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(content, document.Text);
            Assert.Equal(Path.GetFileName(path), document.FileName);
            Assert.Equal(".txt", document.Extension);
            Assert.Equal(path, document.Path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SupportedExtensions_IncludesCommonTextFormats()
    {
        var extractor = new PlainTextExtractor();

        Assert.Contains(".txt", extractor.SupportedExtensions);
        Assert.Contains(".csv", extractor.SupportedExtensions);
        Assert.Contains(".json", extractor.SupportedExtensions);
        Assert.Contains(".xml", extractor.SupportedExtensions);
    }
}
