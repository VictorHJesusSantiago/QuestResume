using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
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
        var exifSummary = TryReadExifSummary(path);

        var combinedText = exifSummary is null
            ? text ?? string.Empty
            : $"[Metadados: {exifSummary}]\n{text}";

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = combinedText,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warning is not null)
        {
            document.Metadata["warning"] = warning;
        }

        if (exifSummary is not null)
        {
            document.Metadata["exif"] = exifSummary;
        }

        return Task.FromResult(document);
    }

    /// <summary>
    /// Reads date taken, camera make/model and GPS coordinates from the image's EXIF data, so
    /// photos can be found by when/where/with-what-device they were taken. Returns <c>null</c>
    /// if the file has no EXIF data (e.g. PNG screenshots) or it can't be parsed.
    /// </summary>
    private static string? TryReadExifSummary(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var parts = new List<string>();

            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var dateTaken = exifSubIfd?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal);
            if (!string.IsNullOrWhiteSpace(dateTaken))
            {
                parts.Add($"Data: {dateTaken}");
            }

            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var make = exifIfd0?.GetDescription(ExifDirectoryBase.TagMake);
            var model = exifIfd0?.GetDescription(ExifDirectoryBase.TagModel);
            if (!string.IsNullOrWhiteSpace(make) || !string.IsNullOrWhiteSpace(model))
            {
                parts.Add($"Câmera: {$"{make} {model}".Trim()}");
            }

            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            var location = gps?.GetGeoLocation();
            if (location is not null)
            {
                parts.Add($"Localização: {location.Latitude.ToString(CultureInfo.InvariantCulture)}, {location.Longitude.ToString(CultureInfo.InvariantCulture)}");
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _ocr.Dispose();
}
