using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests.Integration;

public class AudioTranscriptionExtractorIntegrationTests
{
    [Fact]
    public async Task ExtractAsync_WithRealWhisperModel_TranscribesWavWithoutWarning()
    {
        var modelPath = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_WHISPER_MODEL");

        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            return;
        }

        var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.wav");

        try
        {
            WriteSilentWav(wavPath, seconds: 1, sampleRate: 16000);

            using var extractor = new AudioTranscriptionExtractor(modelPath);
            var document = await extractor.ExtractAsync(wavPath);

            Assert.False(document.Metadata.ContainsKey("warning"), document.Metadata.GetValueOrDefault("warning") ?? string.Empty);
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

        private static void WriteSilentWav(string path, int seconds, int sampleRate)
    {
        var dataSize = seconds * sampleRate * sizeof(short);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); 
        writer.Write((short)1); 
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short)); 
        writer.Write((short)sizeof(short)); 
        writer.Write((short)16); 
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }
}
