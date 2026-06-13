using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class AudioTranscriptionExtractorTests
{
    [Fact]
    public async Task ExtractAsync_WhisperModelNotConfigured_ReturnsEmptyTextWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

        try
        {
            await File.WriteAllBytesAsync(path, []);

            var extractor = new AudioTranscriptionExtractor(modelPath: string.Empty);
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_WhisperModelPathPointsToMissingFile_ReturnsEmptyTextWithWarning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");
        var missingModelPath = Path.Combine(Path.GetTempPath(), $"whisper-{Guid.NewGuid()}.bin");

        try
        {
            await File.WriteAllBytesAsync(path, []);

            var extractor = new AudioTranscriptionExtractor(modelPath: missingModelPath);
            var document = await extractor.ExtractAsync(path);

            Assert.Equal(string.Empty, document.Text);
            Assert.True(document.Metadata.ContainsKey("warning"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SupportedExtensions_OnlyIncludesWav()
    {
        var extractor = new AudioTranscriptionExtractor(modelPath: string.Empty);

        Assert.Contains(".wav", extractor.SupportedExtensions);
        Assert.DoesNotContain(".mp3", extractor.SupportedExtensions);
    }
}
