using System.Text;
using PDFtoImage;
using QuestResume.Core.Models;
using SkiaSharp;
using UglyToad.PdfPig;

namespace QuestResume.Core.Extraction.Extractors;

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
                var pageHadText = !string.IsNullOrWhiteSpace(text);

                if (!pageHadText && _ocr is not null && pdfBytes is not null)
                {
                    
                    
#pragma warning disable CA1416 
                    using var bitmap = Conversion.ToImage(pdfBytes, page: pageIndex);
#pragma warning restore CA1416
                    using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    text = _ocr.TryOcr(imageData.ToArray(), out var warning) ?? string.Empty;
                    ocrWarning ??= warning;
                }
                else
                {
                    
                    
                    
                    
                    
                    
                    text = TableHeuristics.DetectAndFormatTables(text ?? string.Empty);
                }

                
                
                
                
                
                
                if (pageHadText && _ocr is not null)
                {
                    text = AppendEmbeddedImageOcrText(text ?? string.Empty, page, ref ocrWarning);
                }

                
                
                
                if (pageIndex > 0)
                {
                    builder.Append('\f');
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

        private string AppendEmbeddedImageOcrText(string text, UglyToad.PdfPig.Content.Page page, ref string? ocrWarning)
    {
        List<string>? recognized = null;

        foreach (var image in page.GetImages())
        {
            byte[]? pngBytes = null;
            try
            {
                if (image.TryGetPng(out var png))
                {
                    pngBytes = png;
                }
            }
            catch
            {
                
            }

            if (pngBytes is null)
            {
                continue;
            }

            var ocrText = _ocr!.TryOcr(pngBytes, out var warning);
            ocrWarning ??= warning;

            if (!string.IsNullOrWhiteSpace(ocrText))
            {
                recognized ??= new List<string>();
                recognized.Add(ocrText.Trim());
            }
        }

        if (recognized is null || recognized.Count == 0)
        {
            return text;
        }

        return text + "\n[OCR de imagem incorporada na página]\n" + string.Join("\n", recognized);
    }

    public void Dispose() => _ocr?.Dispose();
}
