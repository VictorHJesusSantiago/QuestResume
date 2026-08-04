using System.Text;
using System.Xml.Linq;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class Fb2Extractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".fb2" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();

        try
        {
            var xmlText = await EncodingDetector.ReadAllTextDetectedAsync(path, cancellationToken).ConfigureAwait(false);
            var xml = XDocument.Parse(xmlText);
            var ns = xml.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var builder = new StringBuilder();

            var titleInfo = xml.Descendants(ns + "title-info").FirstOrDefault();
            var bookTitle = titleInfo?.Element(ns + "book-title")?.Value;
            if (!string.IsNullOrWhiteSpace(bookTitle))
            {
                builder.AppendLine(bookTitle);
                builder.AppendLine();
            }

            foreach (var body in xml.Descendants(ns + "body"))
            {
                foreach (var paragraph in body.Descendants(ns + "p"))
                {
                    var value = paragraph.Value.Trim();
                    if (value.Length > 0)
                    {
                        builder.AppendLine(value);
                    }
                }
            }

            text = builder.ToString().Trim();
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao extrair FB2: {ex.Message}");
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warnings.Count > 0)
        {
            document.Metadata["warning"] = string.Join(" | ", warnings);
        }

        return document;
    }
}
