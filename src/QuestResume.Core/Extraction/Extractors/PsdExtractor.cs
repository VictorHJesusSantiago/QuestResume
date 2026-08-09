using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class PsdExtractor : IFileExtractor
{
    private static readonly string[] ColorModeNames =
    {
        "Bitmap", "Escala de cinza", "Paleta indexada", "RGB", "CMYK",
        "Multicanal (5)", "Multicanal (6)", "Duotone", "Lab"
    };

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".psd" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();
        var metadata = new Dictionary<string, string>();

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            var signature = new string(reader.ReadChars(4));
            if (signature != "8BPS")
            {
                warnings.Add("Assinatura PSD inválida (não é um arquivo .psd/.psb padrão).");
            }
            else
            {
                var version = ReadUInt16BE(reader);
                reader.ReadBytes(6); 
                var channels = ReadUInt16BE(reader);
                var height = ReadUInt32BE(reader);
                var width = ReadUInt32BE(reader);
                var depth = ReadUInt16BE(reader);
                var colorMode = ReadUInt16BE(reader);

                var colorModeName = colorMode < ColorModeNames.Length ? ColorModeNames[colorMode] : $"Desconhecido ({colorMode})";

                metadata["width"] = width.ToString();
                metadata["height"] = height.ToString();
                metadata["channels"] = channels.ToString();
                metadata["bitDepth"] = depth.ToString();
                metadata["colorMode"] = colorModeName;
                metadata["psdVersion"] = version.ToString();

                var layerCount = TryReadLayerCount(reader);

                text = $"Imagem Photoshop: {width}x{height} pixels, modo de cor {colorModeName}, " +
                       $"{channels} canal(is), profundidade {depth} bits" +
                       (layerCount.HasValue ? $", {layerCount} camada(s)" : string.Empty) + ".";

                if (layerCount.HasValue)
                {
                    metadata["layerCount"] = layerCount.Value.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao extrair metadados PSD: {ex.Message}");
        }

        metadata["warning"] = warnings.Count > 0 ? string.Join(" | ", warnings) : string.Empty;
        if (warnings.Count == 0) metadata.Remove("warning");

        var document = new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = metadata
        };

        return Task.FromResult(document);
    }

    private static int? TryReadLayerCount(BinaryReader reader)
    {
        try
        {
            
            var colorModeDataLength = ReadUInt32BE(reader);
            reader.ReadBytes((int)colorModeDataLength);

            
            var imageResourcesLength = ReadUInt32BE(reader);
            reader.ReadBytes((int)imageResourcesLength);

            
            
            var layerMaskInfoLength = ReadUInt32BE(reader);
            if (layerMaskInfoLength < 6) return null;

            var layerInfoLength = ReadUInt32BE(reader);
            var layerCountRaw = ReadUInt16BE(reader);
            
            
            
            var layerCount = (short)layerCountRaw;
            return Math.Abs((int)layerCount);
        }
        catch
        {
            return null;
        }
    }

    private static ushort ReadUInt16BE(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(2);
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static uint ReadUInt32BE(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }
}
