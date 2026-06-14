using QuestResume.Core.Extraction.Extractors;

namespace QuestResume.Core.Tests;

public class SubtitleExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Srt_RemovesSequenceNumbersAndTimestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.srt");
        const string srt = "1\n00:00:01,000 --> 00:00:04,000\nOlá mundo\n\n2\n00:00:05,000 --> 00:00:08,000\nEste é um teste\n";

        try
        {
            await File.WriteAllTextAsync(path, srt);

            var extractor = new SubtitleExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Olá mundo", document.Text);
            Assert.Contains("Este é um teste", document.Text);
            Assert.DoesNotContain("-->", document.Text);
            Assert.DoesNotContain("00:00:01", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_Vtt_RemovesHeaderAndTimestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vtt");
        const string vtt = "WEBVTT\n\n00:00:01.000 --> 00:00:04.000\nPrimeira linha\n\n00:00:05.000 --> 00:00:08.000\nSegunda linha\n";

        try
        {
            await File.WriteAllTextAsync(path, vtt);

            var extractor = new SubtitleExtractor();
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("Primeira linha", document.Text);
            Assert.Contains("Segunda linha", document.Text);
            Assert.DoesNotContain("WEBVTT", document.Text);
            Assert.DoesNotContain("-->", document.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractAsync_RepeatedConsecutiveLines_AreDeduplicated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.srt");
        const string srt = "1\n00:00:01,000 --> 00:00:04,000\nRepetido\n\n2\n00:00:05,000 --> 00:00:08,000\nRepetido\n";

        try
        {
            await File.WriteAllTextAsync(path, srt);

            var extractor = new SubtitleExtractor();
            var document = await extractor.ExtractAsync(path);

            var occurrences = document.Text.Split("Repetido").Length - 1;
            Assert.Equal(1, occurrences);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
