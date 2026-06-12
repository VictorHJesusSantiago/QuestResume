using System.Text;
using System.Text.Json;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts the markdown and code cell sources from a Jupyter notebook (.ipynb),
/// discarding outputs/metadata noise so the LLM context stays focused on content.
/// </summary>
public sealed class IpynbExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".ipynb" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        var text = ExtractText(json);

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };
    }

    private static string ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("cells", out var cells))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var cell in cells.EnumerateArray())
        {
            var cellType = cell.TryGetProperty("cell_type", out var typeProp)
                ? typeProp.GetString() ?? "code"
                : "code";

            var source = JoinSource(cell);
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            builder.Append('[').Append(cellType).Append("]\n");
            builder.Append(source).Append("\n\n");
        }

        return builder.ToString();
    }

    private static string JoinSource(JsonElement cell)
    {
        if (!cell.TryGetProperty("source", out var source))
        {
            return string.Empty;
        }

        if (source.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var line in source.EnumerateArray())
            {
                builder.Append(line.GetString());
            }
            return builder.ToString();
        }

        return source.ValueKind == JsonValueKind.String ? source.GetString() ?? string.Empty : string.Empty;
    }
}
