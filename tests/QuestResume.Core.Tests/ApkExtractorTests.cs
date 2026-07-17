using System.IO.Compression;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ApkExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ApkWithoutManifest_ListsZipEntriesWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.apk");

        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("classes.dex");
                using var writer = new BinaryWriter(entry.Open());
                writer.Write(new byte[] { 0x01, 0x02, 0x03 });
            }

            var extractor = new ApkExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("classes.dex", document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
