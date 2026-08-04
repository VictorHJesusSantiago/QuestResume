using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

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

        
        var tag = Encoding.ASCII.GetString(buffer);
        return tag.StartsWith("AC", StringComparison.Ordinal) ? tag : null;
    }
}
