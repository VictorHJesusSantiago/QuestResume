using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class ChmDjvuMetadataExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".chm", ".djvu", ".djv" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var extension = info.Extension.ToLowerInvariant();
        var formatName = extension == ".chm" ? "CHM (Compiled HTML Help)" : "DjVu";

        var metadata = new Dictionary<string, string>
        {
            ["warning"] = $"Suporte limitado para {formatName}: não há biblioteca .NET madura e mantida para " +
                           $"extração de texto deste formato. Apenas metadados básicos do arquivo foram lidos " +
                           $"(tamanho: {info.Length} bytes). Nenhum conteúdo textual foi extraído.",
            ["sizeBytes"] = info.Length.ToString()
        };

        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[4];
            var read = stream.Read(header, 0, header.Length);
            if (read == 4)
            {
                var magic = System.Text.Encoding.ASCII.GetString(header);
                metadata["magicBytes"] = magic;
            }
        }
        catch
        {
            
        }

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = string.Empty,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = metadata
        };

        return Task.FromResult(document);
    }
}
