using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from image files via OCR (Tesseract 5). Requires <c>TessDataPath</c> to be
/// configured with a valid <c>tessdata</c> folder; otherwise returns an empty document with
/// a <c>Metadata["warning"]</c> explaining how to set it up, following the same
/// graceful-degradation pattern as the other extractors.
/// </summary>
public sealed class ImageOcrExtractor : IFileExtractor, IDisposable
{
    private readonly TesseractOcrHelper _ocr;

    public IReadOnlyCollection<string> SupportedExtensions { get; } =
        new[] { ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif" };

    public ImageOcrExtractor(string tessDataPath, string languages)
    {
        _ocr = new TesseractOcrHelper(tessDataPath, languages);
    }

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var bytes = File.ReadAllBytes(path);

        var text = _ocr.TryOcr(bytes, out var warning);

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text ?? string.Empty,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warning is not null)
        {
            document.Metadata["warning"] = warning;
        }

        return Task.FromResult(document);
    }

    public void Dispose() => _ocr.Dispose();
}
