using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class OdpExtractorTests
{
    private const string ContentXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0">
          <office:body>
            <office:presentation>
              <draw:page draw:name="Slide 1">
                <draw:frame>
                  <draw:text-box>
                    <text:p>Título da apresentação</text:p>
                    <text:p>Subtítulo</text:p>
                  </draw:text-box>
                </draw:frame>
              </draw:page>
              <draw:page draw:name="Slide 2">
                <draw:frame>
                  <draw:text-box>
                    <text:p>Segundo slide com conteúdo importante</text:p>
                  </draw:text-box>
                </draw:frame>
              </draw:page>
            </office:presentation>
          </office:body>
        </office:document-content>
        """;

    [Fact]
    public async Task ExtractAsync_ReadsTextFromEachSlide()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.odp");

        try
        {
            CreateOdp(path);

            var extractor = new OdpExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Título da apresentação", document.Text);
            Assert.Contains("Subtítulo", document.Text);
            Assert.Contains("Segundo slide com conteúdo importante", document.Text);
            Assert.Contains("Slide 1", document.Text);
            Assert.Contains("Slide 2", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_MissingContentXml_ReturnsEmptyText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.odp");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("mimetype");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("application/vnd.oasis.opendocument.presentation");
            }

            var extractor = new OdpExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SupportedExtensions_ContainsOdp()
    {
        var extractor = new OdpExtractor();
        Assert.Contains(".odp", extractor.SupportedExtensions);
    }

    [Fact]
    public void ExtractorRegistry_SupportsOdp()
    {
        var registry = new QuestResume.Core.Extraction.ExtractorRegistry();
        Assert.True(registry.IsSupported(".odp"));
    }

    private static void CreateOdp(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("content.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(ContentXml);
    }
}
