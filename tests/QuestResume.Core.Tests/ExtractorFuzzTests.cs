using System.Security.Cryptography;
using QuestResume.Core.Extraction;
using Xunit;

namespace QuestResume.Core.Tests;

public class ExtractorFuzzTests
{
    private static byte[] RandomBytes(int n) => RandomNumberGenerator.GetBytes(n);

    public static IEnumerable<object[]> SupportedExtensions()
    {
        var registry = new ExtractorRegistry();
        foreach (var ext in registry.SupportedExtensions)
        {
            yield return new object[] { ext };
        }
    }

    [Theory]
    [MemberData(nameof(SupportedExtensions))]
    public async Task ExtractAsync_OnGarbageInput_DoesNotThrow(string extension)
    {
        var registry = new ExtractorRegistry();

        
        var payloads = new List<byte[]>
        {
            Array.Empty<byte>(),
            RandomBytes(16),
            RandomBytes(1024),
            RandomBytes(64_000),
            
            new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0x00 },
            
            System.Text.Encoding.UTF8.GetBytes("conteúdo válido\n\0\0").Concat(RandomBytes(200)).ToArray()
        };

        var tempDir = Path.Combine(Path.GetTempPath(), $"qr-fuzz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            for (var i = 0; i < payloads.Count; i++)
            {
                var path = Path.Combine(tempDir, $"fuzz{i}{extension}");
                await File.WriteAllBytesAsync(path, payloads[i]);

                
                var exception = await Record.ExceptionAsync(() => registry.ExtractAsync(path));
                Assert.True(
                    exception is null,
                    $"ExtractAsync lançou {exception?.GetType().Name} para '{extension}' (payload #{i}): {exception?.Message}");
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch {  }
        }
    }
}
