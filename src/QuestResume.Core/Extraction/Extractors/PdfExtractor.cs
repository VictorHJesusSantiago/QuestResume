using System.Text;
using PDFtoImage;
using QuestResume.Core.Models;
using SkiaSharp;
using UglyToad.PdfPig;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from PDF and PDF/A documents using PdfPig. When <paramref name="ocrEnabled"/>
/// (via the constructor) is true and a page yields no embedded text, that page is rasterized
/// with PDFtoImage and run through OCR (Tesseract 5) as a fallback for scanned PDFs.
/// </summary>
public sealed class PdfExtractor : IFileExtractor, IDisposable
{
    private readonly bool _ocrEnabled;
    private readonly TesseractOcrHelper? _ocr;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".pdf" };

    public PdfExtractor(bool ocrEnabled = false, string tessDataPath = "", string ocrLanguages = "por+eng")
    {
        _ocrEnabled = ocrEnabled;
        _ocr = ocrEnabled ? new TesseractOcrHelper(tessDataPath, ocrLanguages) : null;
    }

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        string? ocrWarning = null;

        var pdfBytes = _ocrEnabled ? File.ReadAllBytes(path) : null;

        using (var document = PdfDocument.Open(path))
        {
            var pageIndex = 0;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = page.Text;

                if (string.IsNullOrWhiteSpace(text) && _ocr is not null && pdfBytes is not null)
                {
#pragma warning disable CA1416 // PDFtoImage.Conversion.ToImage is supported on all of this app's target platforms
                    using var bitmap = Conversion.ToImage(pdfBytes, page: pageIndex);
#pragma warning restore CA1416
                    using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    text = _ocr.TryOcr(imageData.ToArray(), out var warning) ?? string.Empty;
                    ocrWarning ??= warning;
                }

                builder.AppendLine(text);
                pageIndex++;
            }
        }

        var result = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = builder.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (ocrWarning is not null)
        {
            result.Metadata["warning"] = ocrWarning;
        }

        return Task.FromResult(result);
    }

    public void Dispose() => _ocr?.Dispose();
}
