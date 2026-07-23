using QuestResume.Core.Extraction.Extractors;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace QuestResume.Core.Tests;

public class ArchiveExtractorTests
{
    [Fact]
    public async Task ExtractAsync_TarGz_ConcatenatesEntryText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tar.gz");

        try
        {
            using (var stream = File.Create(path))
            using (var writer = WriterFactory.OpenWriter(stream, ArchiveType.Tar, new WriterOptions(CompressionType.GZip)))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes("Conteúdo do arquivo compactado");
                using var entryStream = new MemoryStream(bytes);
                writer.Write("nota.txt", entryStream, DateTime.UtcNow);
            }

            var extractor = new ArchiveExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("nota.txt", document.Text);
            Assert.Contains("Conteúdo do arquivo compactado", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
