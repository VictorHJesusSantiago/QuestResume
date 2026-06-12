using QuestResume.Core.Extraction;

namespace QuestResume.Core.Tests;

public class ExtractorRegistryTests
{
    [Theory]
    [InlineData(".txt")]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    [InlineData(".html")]
    [InlineData(".odt")]
    [InlineData(".rtf")]
    [InlineData(".epub")]
    [InlineData(".ipynb")]
    [InlineData(".eml")]
    [InlineData(".msg")]
    public void IsSupported_ReturnsTrueForGroup1Extensions(string extension)
    {
        var registry = new ExtractorRegistry();

        Assert.True(registry.IsSupported(extension));
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedExtension_ReturnsEmptyTextWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.unknownext");

        try
        {
            await File.WriteAllTextAsync(path, "conteúdo binário fictício");

            var registry = new ExtractorRegistry();
            var document = await registry.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
