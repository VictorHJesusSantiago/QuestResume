using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class VideoMetadataExtractorTests
{
    [Fact]
    public void SupportedExtensions_IncludesCommonVideoFormats()
    {
        var extractor = new VideoMetadataExtractor();

        Assert.Contains(".mp4", extractor.SupportedExtensions);
        Assert.Contains(".mkv", extractor.SupportedExtensions);
        Assert.Contains(".avi", extractor.SupportedExtensions);
        Assert.Contains(".mov", extractor.SupportedExtensions);
    }

    [Fact]
    public async Task ExtractAsync_Mkv_ReturnsFileSystemMetadataOnlyWithLimitationNote()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mkv");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });

        try
        {
            var extractor = new VideoMetadataExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(Path.GetFileName(path), document.FileName);
            Assert.Contains("Tamanho:", document.Text);
            Assert.Contains("não implementado", document.Text);
            Assert.False(document.Metadata.ContainsKey("durationSeconds"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_Mp4WithMvhdAtom_ExtractsDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");
        await File.WriteAllBytesAsync(path, BuildMinimalMp4WithMvhd(timescale: 1000, durationUnits: 5000));

        try
        {
            var extractor = new VideoMetadataExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.True(document.Metadata.ContainsKey("durationSeconds"));
            Assert.Equal("5.0", document.Metadata["durationSeconds"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

        private static byte[] BuildMinimalMp4WithMvhd(uint timescale, uint durationUnits)
    {
        using var stream = new MemoryStream();

        
        var mvhdBody = new List<byte>();
        mvhdBody.AddRange(new byte[4]); 
        mvhdBody.AddRange(new byte[4]); 
        mvhdBody.AddRange(new byte[4]); 
        mvhdBody.AddRange(BitConverterBigEndian(timescale));
        mvhdBody.AddRange(BitConverterBigEndian(durationUnits));

        var mvhdSize = 8 + mvhdBody.Count;
        var mvhd = new List<byte>();
        mvhd.AddRange(BitConverterBigEndian((uint)mvhdSize));
        mvhd.AddRange(System.Text.Encoding.ASCII.GetBytes("mvhd"));
        mvhd.AddRange(mvhdBody);

        var moovSize = 8 + mvhd.Count;
        var moov = new List<byte>();
        moov.AddRange(BitConverterBigEndian((uint)moovSize));
        moov.AddRange(System.Text.Encoding.ASCII.GetBytes("moov"));
        moov.AddRange(mvhd);

        stream.Write(moov.ToArray());
        return stream.ToArray();
    }

    private static byte[] BitConverterBigEndian(uint value) => new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    };
}
