using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ZipArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ZipWithTextEntries_ConcatenatesEachEntryWithHeader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entryA = archive.CreateEntry("notas/a.txt");
                using (var writer = new StreamWriter(entryA.Open()))
                {
                    writer.Write("Conteúdo do arquivo A");
                }

                var entryB = archive.CreateEntry("b.csv");
                using (var writer = new StreamWriter(entryB.Open()))
                {
                    writer.Write("col1,col2\nval1,val2");
                }
            }

            var extractor = new ZipArchiveExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("notas/a.txt", document.Text);
            Assert.Contains("Conteúdo do arquivo A", document.Text);
            Assert.Contains("b.csv", document.Text);
            Assert.Contains("col1,col2", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_ZipWithUnsupportedEntry_SkipsItWithoutError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("dados.bin");
                using var writer = new BinaryWriter(entry.Open());
                writer.Write(new byte[] { 0x00, 0x01, 0x02 });
            }

            var extractor = new ZipArchiveExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
