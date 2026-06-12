using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class OdtExtractorTests
{
    private const string ContentXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
          <office:body>
            <office:text>
              <text:h>Título do documento</text:h>
              <text:p>Primeiro parágrafo de teste.</text:p>
              <text:p>Segundo parágrafo de teste.</text:p>
            </office:text>
          </office:body>
        </office:document-content>
        """;

    [Fact]
    public async Task ExtractAsync_ReadsParagraphsAndHeadingsFromContentXml()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.odt");

        try
        {
            CreateOdt(path);

            var extractor = new OdtExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Título do documento", document.Text);
            Assert.Contains("Primeiro parágrafo de teste.", document.Text);
            Assert.Contains("Segundo parágrafo de teste.", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_MissingContentXml_ReturnsEmptyText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.odt");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("mimetype");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("application/vnd.oasis.opendocument.text");
            }

            var extractor = new OdtExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateOdt(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("content.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(ContentXml);
    }
}
