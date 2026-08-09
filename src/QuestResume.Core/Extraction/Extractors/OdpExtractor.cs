using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class OdpExtractor : IFileExtractor
{
    private static readonly XNamespace DrawNs = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace PresentationNs = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".odp" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        return Task.FromResult(new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = ExtractText(path),
            ModifiedUtc = info.LastWriteTimeUtc
        });
    }

    private static string ExtractText(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("content.xml");
        if (entry is null)
        {
            return string.Empty;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        var builder = new StringBuilder();
        var slides = document.Descendants(DrawNs + "page").ToList();
        var slideIndex = 0;

        foreach (var slide in slides)
        {
            slideIndex++;
            var slideName = slide.Attribute(DrawNs + "name")?.Value;
            builder.AppendLine(string.IsNullOrWhiteSpace(slideName)
                ? $"[Slide {slideIndex}]"
                : $"[Slide {slideIndex}: {slideName}]");

            foreach (var paragraph in slide.Descendants(TextNs + "p"))
            {
                var text = paragraph.Value;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }

            foreach (var note in slide.Descendants(PresentationNs + "notes").Elements(TextNs + "p"))
            {
                var text = note.Value;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text);
                }
            }
        }

        return builder.ToString();
    }
}
