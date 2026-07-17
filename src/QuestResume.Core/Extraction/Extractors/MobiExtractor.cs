using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Best-effort parser for MOBI/AZW3 e-books (.mobi, .azw3), based on the publicly documented
/// PalmDOC/MOBI/KF8 container format: a PDB (Palm Database) header holds a list of record
/// offsets; the first record is the PalmDOC/MOBI header (compression type + text record count);
/// the following records hold the book text, either uncompressed (type 1) or PalmDOC-LZ77
/// compressed (type 2). HUFF/CDIC compression (type 17480, used by some older MOBI files) is
/// NOT supported and is reported as a warning — this is a deliberate scope limitation, not a
/// bug: implementing the full Huffman dictionary decoder is out of scope for a "best effort"
/// text extractor. KF8 (.azw3)-specific structures (beyond the shared PalmDOC text records) are
/// also not parsed; only the base PalmDOC text stream is extracted.
/// </summary>
public sealed class MobiExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".mobi", ".azw3" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var text = string.Empty;
        var warnings = new List<string>();

        try
        {
            var bytes = File.ReadAllBytes(path);
            text = ParseMobi(bytes, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao extrair MOBI/AZW3 (parser best-effort, formato binário complexo): {ex.Message}");
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

    private static string ParseMobi(byte[] bytes, List<string> warnings)
    {
        if (bytes.Length < 78)
        {
            warnings.Add("Arquivo pequeno demais para conter um cabeçalho PDB válido.");
            return string.Empty;
        }

        // PDB header: 32-byte name, then attribute/version fields, ending with a 2-byte record
        // count at offset 76.
        var recordCount = ReadUInt16BE(bytes, 76);
        if (recordCount == 0)
        {
            warnings.Add("Nenhum registro PDB encontrado.");
            return string.Empty;
        }

        var recordOffsets = new int[recordCount];
        for (var i = 0; i < recordCount; i++)
        {
            var entryOffset = 78 + i * 8;
            if (entryOffset + 4 > bytes.Length) break;
            recordOffsets[i] = (int)ReadUInt32BE(bytes, entryOffset);
        }

        var firstRecordStart = recordOffsets[0];
        var firstRecordEnd = recordCount > 1 ? recordOffsets[1] : bytes.Length;
        if (firstRecordStart + 8 > bytes.Length)
        {
            warnings.Add("Registro PalmDOC inicial ausente/corrompido.");
            return string.Empty;
        }

        var compressionType = ReadUInt16BE(bytes, firstRecordStart);
        var textRecordCount = ReadUInt16BE(bytes, firstRecordStart + 8);

        if (compressionType == 17480)
        {
            warnings.Add("Compressão HUFF/CDIC não suportada (fora do escopo do parser best-effort); apenas metadados extraídos.");
            return string.Empty;
        }

        if (compressionType != 1 && compressionType != 2)
        {
            warnings.Add($"Tipo de compressão desconhecido ({compressionType}); apenas metadados extraídos.");
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 1; i <= textRecordCount && i < recordCount; i++)
        {
            var start = recordOffsets[i];
            var end = i + 1 < recordCount ? recordOffsets[i + 1] : bytes.Length;
            if (start >= bytes.Length || end > bytes.Length || end <= start) continue;

            var recordBytes = bytes[start..end];
            var decoded = compressionType == 2 ? PalmDocDecompress(recordBytes) : recordBytes;
            builder.Append(Encoding.Latin1.GetString(decoded));
        }

        // Best-effort: PalmDOC text is Latin-1/CP1252-ish by convention; re-decode assuming
        // Windows-1252 to recover accented characters when possible.
        try
        {
            var raw = builder.ToString();
            var rawBytes = Encoding.Latin1.GetBytes(raw);
            return Encoding.GetEncoding(1252).GetString(rawBytes).Trim();
        }
        catch
        {
            return builder.ToString().Trim();
        }
    }

    /// <summary>PalmDOC LZ77-style decompression (documented public format).</summary>
    private static byte[] PalmDocDecompress(byte[] input)
    {
        var output = new List<byte>(input.Length * 2);
        var i = 0;

        while (i < input.Length)
        {
            var b = input[i++];

            if (b == 0)
            {
                output.Add(0);
            }
            else if (b <= 8)
            {
                for (var j = 0; j < b && i < input.Length; j++)
                {
                    output.Add(input[i++]);
                }
            }
            else if (b <= 0x7F)
            {
                output.Add(b);
            }
            else if (b >= 0xC0)
            {
                output.Add((byte)' ');
                output.Add((byte)(b ^ 0x80));
            }
            else
            {
                if (i >= input.Length) break;
                var b2 = input[i++];
                var distance = ((b & 0x3F) << 8 | b2) >> 3;
                var length = (b2 & 0x07) + 3;

                var start = output.Count - distance;
                if (start < 0) break;

                for (var j = 0; j < length; j++)
                {
                    output.Add(output[start + j]);
                }
            }
        }

        return output.ToArray();
    }

    private static ushort ReadUInt16BE(byte[] bytes, int offset) => (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static uint ReadUInt32BE(byte[] bytes, int offset) =>
        (uint)((bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3]);
}
