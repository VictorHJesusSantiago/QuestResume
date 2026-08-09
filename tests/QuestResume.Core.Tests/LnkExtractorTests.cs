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

        writer.Write((uint)0x4C); 
        writer.Write(new byte[]
        {
            0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
        }); 
        writer.Write((uint)0x2); 

        
        writer.Write(new byte[52]);

        
        var pathBytes = Encoding.ASCII.GetBytes(targetPath + "\0");
        const int fixedFieldsSize = 28;
        var linkInfoSize = (uint)(fixedFieldsSize + pathBytes.Length);

        writer.Write(linkInfoSize);
        writer.Write((uint)fixedFieldsSize); 
        writer.Write((uint)0x1); 
        writer.Write((uint)fixedFieldsSize); 
        writer.Write((uint)fixedFieldsSize); 
        writer.Write((uint)0); 
        writer.Write((uint)fixedFieldsSize); 

        writer.Write(pathBytes);

        return stream.ToArray();
    }
}
