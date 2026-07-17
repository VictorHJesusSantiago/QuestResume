using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class PsdExtractorTests
{
    [Fact]
    public async Task ExtractAsync_PsdHeader_ExtractsDimensionsAndColorMode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.psd");

        try
        {
            await File.WriteAllBytesAsync(path, BuildPsd(width: 800, height: 600, channels: 3, colorMode: 3, layerCount: 2));

            var extractor = new PsdExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("800x600", document.Text);
            Assert.Contains("RGB", document.Text);
            Assert.Equal("800", document.Metadata["width"]);
            Assert.Equal("600", document.Metadata["height"]);
            Assert.Equal("2", document.Metadata["layerCount"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildPsd(int width, int height, ushort channels, ushort colorMode, short layerCount)
    {
        using var stream = new MemoryStream();

        void WriteU16(ushort v) { stream.WriteByte((byte)(v >> 8)); stream.WriteByte((byte)v); }
        void WriteU32(uint v)
        {
            stream.WriteByte((byte)(v >> 24));
            stream.WriteByte((byte)(v >> 16));
            stream.WriteByte((byte)(v >> 8));
            stream.WriteByte((byte)v);
        }

        stream.Write(System.Text.Encoding.ASCII.GetBytes("8BPS"));
        WriteU16(1); // version
        stream.Write(new byte[6]); // reserved
        WriteU16(channels);
        WriteU32((uint)height);
        WriteU32((uint)width);
        WriteU16(8); // depth
        WriteU16(colorMode);

        WriteU32(0); // Color Mode Data length
        WriteU32(0); // Image Resources length
        WriteU32(6); // Layer and Mask Information length (just enough for the fields below)
        WriteU32(2); // Layer Info length
        WriteU16((ushort)layerCount);

        return stream.ToArray();
    }
}
