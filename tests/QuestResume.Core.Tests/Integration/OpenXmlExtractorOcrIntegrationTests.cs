using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestResume.Core.Extraction.Extractors;
using SkiaSharp;

namespace QuestResume.Core.Tests.Integration;

/// <summary>
/// Opt-in tests that exercise item 17 (OCR of images embedded in .docx) against a real
/// Tesseract installation. Skipped (pass trivially) unless
/// <c>QUESTRESUME_TEST_TESSDATA_PATH</c> points to an existing <c>tessdata</c> folder — same
/// convention as <see cref="ImageOcrExtractorIntegrationTests"/>.
/// </summary>
public class OpenXmlExtractorOcrIntegrationTests
{
    [Fact]
    public async Task ExtractAsync_Docx_WithEmbeddedImage_RecognizesTextViaOcr()
    {
        var tessDataPath = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_TESSDATA_PATH");
        if (string.IsNullOrWhiteSpace(tessDataPath) || !Directory.Exists(tessDataPath))
        {
            return;
        }

        var languages = Environment.GetEnvironmentVariable("QUESTRESUME_TEST_OCR_LANGUAGES") ?? "eng";
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.docx");
        var imageBytes = CreateTextImagePng("QuestResume");

        try
        {
            CreateDocxWithEmbeddedImage(path, imageBytes);

            using var extractor = new OpenXmlExtractor(ocrEnabled: true, tessDataPath, languages);
            var document = await extractor.ExtractAsync(path);

            Assert.Contains("QuestResume", document.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] CreateTextImagePng(string text)
    {
        using var bitmap = new SKBitmap(400, 100);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 48);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 10, 60, SKTextAlign.Left, font, paint);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void CreateDocxWithEmbeddedImage(string path, byte[] pngBytes)
    {
        using var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(new Paragraph(new Run(new Text("Documento com imagem incorporada.")))));

        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(pngBytes))
        {
            imagePart.FeedData(stream);
        }

        mainPart.Document.Save();
    }
}
