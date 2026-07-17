using System.Text;
using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class LnkExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ShellLink_ExtractsTargetPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.lnk");
        const string target = @"C:\Users\teste\documento.txt";

        try
        {
            await File.WriteAllBytesAsync(path, BuildLnk(target));

            var extractor = new LnkExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Atalho para:", document.Text);
            Assert.Contains(target, document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildLnk(string targetPath)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((uint)0x4C); // HeaderSize
        writer.Write(new byte[]
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
        }); // LinkCLSID
        writer.Write((uint)0x2); // LinkFlags: HasLinkInfo only

        // Remaining header fields (offset 24 to 76 = 52 bytes) irrelevant to this test.
        writer.Write(new byte[52]);

        // LinkInfo structure.
        var pathBytes = Encoding.ASCII.GetBytes(targetPath + "\0");
        const int fixedFieldsSize = 28;
        var linkInfoSize = (uint)(fixedFieldsSize + pathBytes.Length);

        writer.Write(linkInfoSize);
        writer.Write((uint)fixedFieldsSize); // LinkInfoHeaderSize
        writer.Write((uint)0x1); // LinkInfoFlags: HasLocalBasePath
        writer.Write((uint)fixedFieldsSize); // VolumeIDOffset (unused by parser, points past fixed fields)
        writer.Write((uint)fixedFieldsSize); // LocalBasePathOffset (relative to LinkInfo start)
        writer.Write((uint)0); // CommonNetworkRelativeLinkOffset
        writer.Write((uint)fixedFieldsSize); // CommonPathSuffixOffset

        writer.Write(pathBytes);

        return stream.ToArray();
    }
}
