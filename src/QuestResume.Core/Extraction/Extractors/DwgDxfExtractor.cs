using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// .dxf (AutoCAD Drawing Exchange Format) is a documented plain-text/ASCII group-code format:
/// this extractor scans for TEXT/MTEXT entities and pulls their string content (group code 1,
/// and continuation group code 3 for MTEXT). .dwg is AutoCAD's proprietary binary format with
/// no mature open-source .NET parser available; for .dwg only the fixed-position version tag in
/// the file header is read (best-effort metadata), with a clear PT-BR warning that no text
/// content could be extracted — same graceful-degradation pattern as
/// <see cref="ChmDjvuMetadataExtractor"/>.
/// </summary>
public sealed class DwgDxfExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".dxf", ".dwg" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();
        var metadata = new Dictionary<string, string>();

        try
        {
            if (info.Extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                text = await ExtractDxfTextAsync(path, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                {
                    warnings.Add("Nenhuma entidade TEXT/MTEXT encontrada no arquivo DXF.");
                }
            }
            else
            {
                var version = TryReadDwgVersion(path);
                metadata["dwgVersionTag"] = version ?? "desconhecida";
                warnings.Add("Suporte limitado para .dwg: formato binário proprietário sem biblioteca .NET open-source " +
                              "madura disponível. Apenas a marca de versão do cabeçalho foi lida" +
                              (version is not null ? $" ({version})" : string.Empty) +
                              "; nenhum texto/entidade foi extraído. Considere exportar para .dxf no AutoCAD para " +
                              "indexação completa de texto.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao extrair arquivo CAD ({info.Extension}): {ex.Message}");
        }

        if (warnings.Count > 0)
        {
            metadata["warning"] = string.Join(" | ", warnings);
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = metadata
        };
    }

    private static async Task<string> ExtractDxfTextAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder();
        var insideTextEntity = false;

        for (var i = 0; i + 1 < lines.Length; i += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var groupCode = lines[i].Trim();
            var value = lines[i + 1].Trim();

            if (groupCode == "0")
            {
                insideTextEntity = value is "TEXT" or "MTEXT";
                continue;
            }

            if (insideTextEntity && (groupCode == "1" || groupCode == "3"))
            {
                // MTEXT stores its content across multiple group-code-3 "continuation" lines,
                // with the final chunk under group code 1; \P inside MTEXT strings marks a
                // paragraph break per the DXF spec.
                builder.AppendLine(value.Replace("\\P", "\n"));
            }
        }

        return builder.ToString().Trim();
    }

    private static string? TryReadDwgVersion(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[6];
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read < 6) return null;

        // DWG files start with an ASCII version tag like "AC1027" (AutoCAD 2013+), "AC1021" etc.
        var tag = Encoding.ASCII.GetString(buffer);
        return tag.StartsWith("AC", StringComparison.Ordinal) ? tag : null;
    }
}
