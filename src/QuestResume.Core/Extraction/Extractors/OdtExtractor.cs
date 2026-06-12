using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from OpenDocument Text (.odt) files. An .odt is a zip archive containing
/// a content.xml with the document body, using the ODF "text" namespace for paragraphs
/// and headings.
/// </summary>
public sealed class OdtExtractor : IFileExtractor
{
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".odt" };

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
        var paragraphsAndHeadings = document.Descendants()
            .Where(e => e.Name == TextNs + "p" || e.Name == TextNs + "h");

        foreach (var element in paragraphsAndHeadings)
        {
            builder.AppendLine(element.Value);
        }

        return builder.ToString();
    }
}
