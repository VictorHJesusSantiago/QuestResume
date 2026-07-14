using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class ExecutableMetadataExtractorTests
{
    [Fact]
    public void SupportedExtensions_IncludesExeAndDll()
    {
        var extractor = new ExecutableMetadataExtractor();

        Assert.Contains(".exe", extractor.SupportedExtensions);
        Assert.Contains(".dll", extractor.SupportedExtensions);
    }

    [Fact]
    public async Task ExtractAsync_RealManagedAssembly_ReadsCoffTimestampAndFlagsAsManaged()
    {
        // QuestResume.Core.dll itself is a real, currently-built .NET assembly (PE + CLR
        // metadata) — using it avoids depending on an external test fixture binary.
        var path = typeof(ExecutableMetadataExtractor).Assembly.Location;

        var extractor = new ExecutableMetadataExtractor();
        var document = await extractor.ExtractAsync(path);

        Assert.Equal("True", document.Metadata["isManagedAssembly"]);
        Assert.True(document.Metadata.ContainsKey("compileTimeUtc"));
        Assert.Contains("Data de compilação", document.Text);
    }

    [Fact]
    public async Task ExtractAsync_NotAValidPeFile_DoesNotThrowAndReportsWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.exe");
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });

        try
        {
            var extractor = new ExecutableMetadataExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.True(document.Metadata.ContainsKey("peWarning"));
            Assert.Contains("Tamanho:", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
