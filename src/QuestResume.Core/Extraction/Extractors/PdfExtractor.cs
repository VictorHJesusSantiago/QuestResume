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
                var pageHadText = !string.IsNullOrWhiteSpace(text);

                if (!pageHadText && _ocr is not null && pdfBytes is not null)
                {
                    // Whole page has no extractable text at all (typical of a scanned page):
                    // rasterize the full page and OCR it.
#pragma warning disable CA1416 // PDFtoImage.Conversion.ToImage is supported on all of this app's target platforms
                    using var bitmap = Conversion.ToImage(pdfBytes, page: pageIndex);
#pragma warning restore CA1416
                    using var imageData = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    text = _ocr.TryOcr(imageData.ToArray(), out var warning) ?? string.Empty;
                    ocrWarning ??= warning;
                }
                else
                {
                    // Item 16: heuristic table detection over the page's extracted text. Purely
                    // text-based (looks for runs of consecutive lines that split into the same
                    // number of columns on 2+ spaces/tabs) — it is NOT guaranteed to catch every
                    // real table (borderless single-column-looking tables, tables with wrapped
                    // cell text, etc. can be missed), but it upgrades the common case of a
                    // simple grid into Markdown instead of flattened space-separated text.
                    text = TableHeuristics.DetectAndFormatTables(text ?? string.Empty);
                }

                // Item 17: even when a page already has embedded text, it can still contain
                // embedded raster images (e.g. a scanned chart pasted next to real paragraph
                // text) whose content isn't part of page.Text. When OCR is enabled, run it over
                // every embedded image on the page too and append the recognized text — this is
                // in addition to (not a replacement for) the whole-page OCR fallback above, which
                // only fires when the page has NO extractable text at all.
                if (pageHadText && _ocr is not null)
                {
                    text = AppendEmbeddedImageOcrText(text ?? string.Empty, page, ref ocrWarning);
                }

                // Form-feed marks a page boundary so TextChunker can recover the page number
                // each chunk starts on. Stripped from the visible text again by TextChunker
                // before it's stored/searched/displayed.
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

    /// <summary>
    /// Item 17: runs OCR over every raster image embedded in <paramref name="page"/> (via
    /// PdfPig's <c>Page.GetImages()</c>) and appends the recognized text after the page's
    /// regular extracted text. Best-effort: images PdfPig can't decode to PNG (unsupported
    /// filter/colorspace) are silently skipped rather than failing the whole page.
    /// </summary>
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
                // Unsupported image encoding for this particular embedded image — skip it.
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
