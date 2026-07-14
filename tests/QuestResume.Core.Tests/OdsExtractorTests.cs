using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class OdsExtractorTests
{
    private const string ContentXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content
            xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
          <office:body>
            <office:spreadsheet>
              <table:table table:name="Planilha1">
                <table:table-row>
                  <table:table-cell><text:p>Nome</text:p></table:table-cell>
                  <table:table-cell><text:p>Idade</text:p></table:table-cell>
                </table:table-row>
                <table:table-row>
                  <table:table-cell><text:p>Maria</text:p></table:table-cell>
                  <table:table-cell><text:p>32</text:p></table:table-cell>
                </table:table-row>
              </table:table>
            </office:spreadsheet>
          </office:body>
        </office:document-content>
        """;

    [Fact]
    public async Task ExtractAsync_ReadsCellsRowByRow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ods");

        try
        {
            CreateOds(path);

            var extractor = new OdsExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Planilha1", document.Text);
            Assert.Contains("Nome", document.Text);
            Assert.Contains("Maria", document.Text);
            Assert.Contains("32", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_MissingContentXml_ReturnsEmptyText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ods");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("mimetype");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("application/vnd.oasis.opendocument.spreadsheet");
            }

            var extractor = new OdsExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SupportedExtensions_ContainsOds()
    {
        var extractor = new OdsExtractor();
        Assert.Contains(".ods", extractor.SupportedExtensions);
    }

    private static void CreateOds(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("content.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(ContentXml);
    }
}
