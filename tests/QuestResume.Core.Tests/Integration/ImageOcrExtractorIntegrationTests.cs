using QuestResume.Core.Extraction.Extractors;
using SkiaSharp;

namespace QuestResume.Core.Tests.Integration;

/// <summary>
/// Opt-in tests that exercise <see cref="ImageOcrExtractor"/> against a real Tesseract
/// installation. Skipped (pass trivially) unless <c>QUESTRESUME_TEST_TESSDATA_PATH</c> points to
/// an existing <c>tessdata</c> folder.
/// </summary>
public class ImageOcrExtractorIntegrationTests
{
    [Fact]
    public async Task ExtractAsync_WithRealTessdata_RecognizesRenderedText()
    {
        var tessDataPath = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_TESSDATA_PATH");

        if (string.IsNullOrWhiteSpace(tessDataPath) || !Directory.Exists(tessDataPath))
        {
            return;
        }

        var languages = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_OCR_LANGUAGES") ?? "eng";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

        try
        {
            CreateTextImage(path, "QuestResume");

            using var extractor = new ImageOcrExtractor(tessDataPath, languages);
            var document = await extractor.ExtractAsync(path);

            Assert.False(document.Metadata.ContainsKey("warning"), document.Metadata.GetValueOrDefault("warning") ?? string.Empty);
            Assert.Contains("QuestResume", document.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Renders <paramref name="text"/> on a white background and saves it as a PNG.</summary>
    private static void CreateTextImage(string path, string text)
    {
        using var bitmap = new SKBitmap(400, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 48);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        canvas.DrawText(text, 10, 60, SKTextAlign.Left, font, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
