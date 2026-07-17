using System.Text;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class DwgDxfExtractorTests
{
    [Fact]
    public async Task ExtractAsync_DxfFile_ExtractsTextEntity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.dxf");
        const string dxf = "0\nTEXT\n1\nConteúdo do desenho\n0\nENDSEC\n";

        try
        {
            await File.WriteAllTextAsync(path, dxf);

            var extractor = new DwgDxfExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Conteúdo do desenho", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_DwgFile_ReturnsLimitedSupportWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.dwg");

        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("AC1027" + new string('\0', 20)));

            var extractor = new DwgDxfExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
            Assert.Equal("AC1027", document.Metadata["dwgVersionTag"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
