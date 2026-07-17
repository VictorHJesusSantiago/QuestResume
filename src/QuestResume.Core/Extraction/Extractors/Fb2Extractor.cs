using System.Text;
using System.Xml.Linq;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from FictionBook 2 (.fb2) e-books. FB2 is a plain XML format, so it's parsed
/// directly with <see cref="XDocument"/> (similar in spirit to <see cref="EpubExtractor"/>, but
/// without the container/OPF indirection an EPUB needs), concatenating every &lt;p&gt; element's
/// text under &lt;body&gt;.
/// </summary>
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
