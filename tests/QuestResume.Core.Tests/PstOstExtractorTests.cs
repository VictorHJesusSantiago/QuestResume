using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class PstOstExtractorTests
{
    [Fact]
    public async Task ExtractAsync_InvalidPstFile_DegradesGracefullyWithWarning()
    {
        // Building a real, valid .pst binary from scratch is impractical for a unit test (it's a
        // complex proprietary format); this test exercises the graceful-degradation path that
        // kicks in for a corrupt/unreadable file, which XstReader is expected to reject.
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pst");

        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x21, 0x42, 0x44, 0x4E, 0x00, 0x00 });

            var extractor = new PstOstExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.True(document.Metadata.ContainsKey("warning"));
            Assert.Equal("0", document.Metadata["emailCount"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
