using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class MobiExtractorTests
{
    [Fact]
    public async Task ExtractAsync_MinimalUncompressedMobi_ExtractsText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mobi");

        try
        {
            var bytes = BuildMinimalMobi("Texto de exemplo do livro MOBI.");
            await File.WriteAllBytesAsync(path, bytes);

            var extractor = new MobiExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Texto de exemplo do livro MOBI.", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] BuildMinimalMobi(string text)
    {
        var textBytes = System.Text.Encoding.Latin1.GetBytes(text);

        using var stream = new MemoryStream();

        
        stream.Write(new byte[76]);
        WriteUInt16BE(stream, 2); 

        
        var recordListOffset = (int)stream.Position;
        stream.Write(new byte[16]);

        var firstRecordStart = (int)stream.Position;

        
        WriteUInt16BE(stream, 1); 
        WriteUInt16BE(stream, 0); 
        WriteUInt32BE(stream, (uint)textBytes.Length);
        WriteUInt16BE(stream, 1); 
        WriteUInt16BE(stream, 4096); 
        WriteUInt16BE(stream, 0); 
        WriteUInt16BE(stream, 0); 

        var secondRecordStart = (int)stream.Position;

        
        stream.Write(textBytes);

        var result = stream.ToArray();

        
        WriteBackfillOffset(result, recordListOffset, (uint)firstRecordStart);
        WriteBackfillOffset(result, recordListOffset + 8, (uint)secondRecordStart);

        return result;
    }

    private static void WriteBackfillOffset(byte[] buffer, int position, uint value)
    {
        var offsetBytes = BitConverter.GetBytes(value);
        Array.Reverse(offsetBytes);
        Array.Copy(offsetBytes, 0, buffer, position, 4);
    }

    private static void WriteUInt16BE(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
