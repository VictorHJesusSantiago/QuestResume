using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Parses Windows Shell Link (.lnk) binary files to recover the shortcut's target path, per the
/// publicly documented MS-SHLLINK format: a fixed 76-byte header (starting with the
/// well-known CLSID <c>00021401-0000-0000-C000-000000000046</c>), optionally followed by an
/// IDList, and (when the HasLinkInfo flag is set) a LinkInfo structure containing the local
/// base path of the target.
/// </summary>
public sealed class LnkExtractor : IFileExtractor
{
    private static readonly byte[] LnkGuid =
    {
        0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46
    };

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".lnk" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();

        try
        {
            var bytes = File.ReadAllBytes(path);
            var target = ParseTarget(bytes);
            text = target is not null ? $"Atalho para: {target}" : string.Empty;
            if (target is null)
            {
                warnings.Add("Não foi possível localizar o caminho de destino no atalho.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao analisar arquivo .lnk: {ex.Message}");
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

        return Task.FromResult(document);
    }

    private static string? ParseTarget(byte[] bytes)
    {
        if (bytes.Length < 76) return null;

        // HeaderSize (4 bytes, must be 0x0000004C) + LinkCLSID (16 bytes).
        var headerSize = BitConverter.ToUInt32(bytes, 0);
        if (headerSize != 0x4C) return null;

        for (var i = 0; i < LnkGuid.Length; i++)
        {
            if (bytes[4 + i] != LnkGuid[i]) return null;
        }

        var linkFlags = BitConverter.ToUInt32(bytes, 20);
        var hasLinkTargetIdList = (linkFlags & 0x1) != 0;
        var hasLinkInfo = (linkFlags & 0x2) != 0;

        var offset = 76;

        if (hasLinkTargetIdList)
        {
            if (offset + 2 > bytes.Length) return null;
            var idListSize = BitConverter.ToUInt16(bytes, offset);
            offset += 2 + idListSize;
        }

        if (!hasLinkInfo || offset + 4 > bytes.Length) return null;

        var linkInfoStart = offset;
        var linkInfoSize = BitConverter.ToUInt32(bytes, linkInfoStart);
        if (linkInfoSize < 4 || linkInfoStart + linkInfoSize > bytes.Length) return null;

        // LinkInfoHeaderSize at offset 4, LinkInfoFlags at offset 8.
        var linkInfoFlags = BitConverter.ToUInt32(bytes, linkInfoStart + 8);
        var hasLocalBasePath = (linkInfoFlags & 0x1) != 0;
        if (!hasLocalBasePath) return null;

        // LocalBasePathOffset at offset 16 (relative to linkInfoStart).
        var localBasePathOffset = BitConverter.ToUInt32(bytes, linkInfoStart + 16);
        var pathStart = linkInfoStart + (int)localBasePathOffset;
        if (pathStart < 0 || pathStart >= bytes.Length) return null;

        var end = pathStart;
        while (end < bytes.Length && bytes[end] != 0) end++;

        return Encoding.ASCII.GetString(bytes, pathStart, end - pathStart);
    }
}
