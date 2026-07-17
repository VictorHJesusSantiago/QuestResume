using System.Text;
using Microsoft.Data.Sqlite;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts a Markdown representation (schema + up to <see cref="MaxRowsPerTable"/> sample rows)
/// of every user table in a SQLite database (.sqlite, .db). Uses Microsoft.Data.Sqlite, already a
/// project dependency (see <see cref="QuestResume.Core.Embeddings.VectorStore"/>).
/// </summary>
public sealed class SqliteExtractor : IFileExtractor
{
    private const int MaxRowsPerTable = 100;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".sqlite", ".db" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            connection.Open();

            var tableNames = new List<string>();
            using (var listCmd = connection.CreateCommand())
            {
                listCmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using var reader = listCmd.ExecuteReader();
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = reader.GetString(0);
                    var sql = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    tableNames.Add(name);

                    builder.AppendLine($"## Tabela: {name}");
                    if (!string.IsNullOrWhiteSpace(sql))
                    {
                        builder.AppendLine("```sql");
                        builder.AppendLine(sql);
                        builder.AppendLine("```");
                    }
                    builder.AppendLine();
                }
            }

            foreach (var table in tableNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var cmd = connection.CreateCommand();
                    // Nomes de tabela não podem ser parametrizados; escapadas entre aspas duplas
                    // (identificador SQLite) para evitar SQL injection via nome de tabela malicioso.
                    var safeName = table.Replace("\"", "\"\"");
                    cmd.CommandText = $"SELECT * FROM \"{safeName}\" LIMIT {MaxRowsPerTable};";
                    using var reader = cmd.ExecuteReader();

                    var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
                    builder.AppendLine($"### Amostra de dados: {table} (até {MaxRowsPerTable} linhas)");
                    builder.AppendLine("| " + string.Join(" | ", columns) + " |");
                    builder.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

                    var rowCount = 0;
                    while (reader.Read())
                    {
                        var values = new string[reader.FieldCount];
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            values[i] = reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString()?.Replace("|", "\\|") ?? string.Empty;
                        }
                        builder.AppendLine("| " + string.Join(" | ", values) + " |");
                        rowCount++;
                    }

                    if (rowCount == 0)
                    {
                        builder.AppendLine("(tabela vazia)");
                    }
                    builder.AppendLine();
                }
                catch (Exception ex)
                {
                    warnings.Add($"Falha ao ler a tabela '{table}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao abrir o banco SQLite: {ex.Message}");
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

        return Task.FromResult(document);
    }
}
