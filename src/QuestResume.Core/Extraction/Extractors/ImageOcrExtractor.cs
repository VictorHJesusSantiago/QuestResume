using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

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
