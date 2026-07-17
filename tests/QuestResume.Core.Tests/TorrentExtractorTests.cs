using System.Text;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class TorrentExtractorTests
{
    [Fact]
    public async Task ExtractAsync_TorrentFile_ExtractsNameTrackerAndSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.torrent");

        // d8:announce20:http://tracker.test4:infod6:lengthi12345e4:name8:teste.txtee
        const string bencode =
            "d8:announce20:http://tracker.test/4:infod6:lengthi12345e4:name9:teste.txtee";

        try
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes(bencode));

            var extractor = new TorrentExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("http://tracker.test/", document.Text);
            Assert.Contains("teste.txt", document.Text);
            Assert.Contains("12345", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
