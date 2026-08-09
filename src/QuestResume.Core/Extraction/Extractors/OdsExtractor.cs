using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class OdsExtractor : IFileExtractor
{
    private static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".ods" };

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

        foreach (var table in document.Descendants(TableNs + "table"))
        {
            var tableName = table.Attribute(TableNs + "name")?.Value;
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                builder.AppendLine($"[Planilha: {tableName}]");
            }

            foreach (var row in table.Descendants(TableNs + "table-row"))
            {
                var cellTexts = new List<string>();
                foreach (var cell in row.Elements(TableNs + "table-cell"))
                {
                    var cellText = string.Join(" ", cell.Descendants(TextNs + "p").Select(p => p.Value));

                    
                    
                    
                    var repeatAttr = cell.Attribute(TableNs + "number-columns-repeated");
                    var repeat = repeatAttr is not null && int.TryParse(repeatAttr.Value, out var r) ? r : 1;

                    if (string.IsNullOrEmpty(cellText) && repeat > 1)
                    {
                        continue;
                    }

                    for (var i = 0; i < repeat; i++)
                    {
                        cellTexts.Add(cellText);
                    }
                }

                if (cellTexts.Any(c => !string.IsNullOrWhiteSpace(c)))
                {
                    builder.AppendLine(string.Join('\t', cellTexts));
                }
            }
        }

        return builder.ToString();
    }
}
