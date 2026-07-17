using System.Text;
using Parquet;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts a Markdown representation (schema + up to <see cref="MaxRowsPerFile"/> sample rows)
/// of an Apache Parquet file, via Parquet.Net.
/// </summary>
public sealed class ParquetExtractor : IFileExtractor
{
    private const int MaxRowsPerFile = 100;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".parquet" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            var fields = reader.Schema.GetDataFields();
            builder.AppendLine("## Esquema");
            foreach (var field in fields)
            {
                builder.AppendLine($"- {field.Name} ({field.ClrType.Name})");
            }
            builder.AppendLine();

            builder.AppendLine($"## Amostra de dados (até {MaxRowsPerFile} linhas)");
            builder.AppendLine("| " + string.Join(" | ", fields.Select(f => f.Name)) + " |");
            builder.AppendLine("| " + string.Join(" | ", fields.Select(_ => "---")) + " |");

            var rowsWritten = 0;
            for (var i = 0; i < reader.RowGroupCount && rowsWritten < MaxRowsPerFile; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rowGroup = reader.OpenRowGroupReader(i);
                var columns = new object?[fields.Length][];
                for (var f = 0; f < fields.Length; f++)
                {
                    var column = await rowGroup.ReadColumnAsync(fields[f], cancellationToken).ConfigureAwait(false);
                    columns[f] = column.Data as object?[] ?? column.Data.Cast<object?>().ToArray();
                }

                var rowCountInGroup = columns.Length > 0 ? columns[0]?.Length ?? 0 : 0;
                for (var r = 0; r < rowCountInGroup && rowsWritten < MaxRowsPerFile; r++)
                {
                    var values = columns.Select(c => c is not null && r < c.Length ? (c[r]?.ToString()?.Replace("|", "\\|") ?? string.Empty) : string.Empty);
                    builder.AppendLine("| " + string.Join(" | ", values) + " |");
                    rowsWritten++;
                }
            }

            if (rowsWritten == 0)
            {
                builder.AppendLine("(arquivo sem linhas)");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao ler arquivo Parquet: {ex.Message}");
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = builder.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc
        };

        if (warnings.Count > 0)
        {
            document.Metadata["warning"] = string.Join(" | ", warnings);
        }

        return document;
    }
}
