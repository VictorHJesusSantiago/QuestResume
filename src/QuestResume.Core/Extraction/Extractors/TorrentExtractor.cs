using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts metadata (name, total size, file list, trackers) from .torrent files via a minimal
/// bencode parser (the format .torrent files are encoded in: byte strings as
/// <c>&lt;length&gt;:&lt;bytes&gt;</c>, integers as <c>i&lt;n&gt;e</c>, lists as
/// <c>l...e</c> and dictionaries as <c>d...e</c>).
/// </summary>
public sealed class TorrentExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".torrent" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        try
        {
            var bytes = File.ReadAllBytes(path);
            var pos = 0;
            var root = BencodeParser.Parse(bytes, ref pos) as Dictionary<string, object?>;

            if (root is null)
            {
                warnings.Add("Estrutura bencode raiz não é um dicionário; arquivo .torrent inválido.");
            }
            else
            {
                if (root.TryGetValue("announce", out var announce) && announce is byte[] announceBytes)
                {
                    builder.AppendLine($"Tracker principal: {Encoding.UTF8.GetString(announceBytes)}");
                }

                if (root.TryGetValue("announce-list", out var announceList) && announceList is List<object?> trackerTiers)
                {
                    builder.AppendLine("Trackers adicionais:");
                    foreach (var tier in trackerTiers)
                    {
                        if (tier is List<object?> tierList)
                        {
                            foreach (var tracker in tierList)
                            {
                                if (tracker is byte[] tb)
                                {
                                    builder.AppendLine($"  - {Encoding.UTF8.GetString(tb)}");
                                }
                            }
                        }
                    }
                }

                if (root.TryGetValue("info", out var infoObj) && infoObj is Dictionary<string, object?> infoDict)
                {
                    var name = infoDict.TryGetValue("name", out var n) && n is byte[] nb ? Encoding.UTF8.GetString(nb) : "(sem nome)";
                    builder.AppendLine($"Nome: {name}");

                    if (infoDict.TryGetValue("length", out var lengthObj) && lengthObj is long singleFileLength)
                    {
                        builder.AppendLine($"Tamanho total: {singleFileLength} bytes");
                    }
                    else if (infoDict.TryGetValue("files", out var filesObj) && filesObj is List<object?> files)
                    {
                        long total = 0;
                        builder.AppendLine("Arquivos:");
                        foreach (var f in files)
                        {
                            if (f is not Dictionary<string, object?> fileDict) continue;

                            var length = fileDict.TryGetValue("length", out var lo) && lo is long l ? l : 0;
                            total += length;

                            var pathParts = fileDict.TryGetValue("path", out var po) && po is List<object?> pl
                                ? string.Join("/", pl.OfType<byte[]>().Select(Encoding.UTF8.GetString))
                                : "(caminho desconhecido)";

                            builder.AppendLine($"  - {pathParts} ({length} bytes)");
                        }
                        builder.AppendLine($"Tamanho total: {total} bytes");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao analisar arquivo .torrent: {ex.Message}");
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

/// <summary>Minimal bencode decoder (strings as raw <c>byte[]</c>, integers as <c>long</c>).</summary>
internal static class BencodeParser
{
    public static object? Parse(byte[] data, ref int pos)
    {
        if (pos >= data.Length) return null;

        return (char)data[pos] switch
        {
            'i' => ParseInteger(data, ref pos),
            'l' => ParseList(data, ref pos),
            'd' => ParseDictionary(data, ref pos),
            _ => ParseByteString(data, ref pos)
        };
    }

    private static long ParseInteger(byte[] data, ref int pos)
    {
        pos++; // skip 'i'
        var start = pos;
        while (data[pos] != 'e') pos++;
        var value = long.Parse(Encoding.ASCII.GetString(data, start, pos - start));
        pos++; // skip 'e'
        return value;
    }

    private static byte[] ParseByteString(byte[] data, ref int pos)
    {
        var start = pos;
        while (data[pos] != ':') pos++;
        var length = int.Parse(Encoding.ASCII.GetString(data, start, pos - start));
        pos++; // skip ':'
        var result = data[pos..(pos + length)];
        pos += length;
        return result;
    }

    private static List<object?> ParseList(byte[] data, ref int pos)
    {
        pos++; // skip 'l'
        var list = new List<object?>();
        while (data[pos] != 'e')
        {
            list.Add(Parse(data, ref pos));
        }
        pos++; // skip 'e'
        return list;
    }

    private static Dictionary<string, object?> ParseDictionary(byte[] data, ref int pos)
    {
        pos++; // skip 'd'
        var dict = new Dictionary<string, object?>();
        while (data[pos] != 'e')
        {
            var key = ParseByteString(data, ref pos);
            var value = Parse(data, ref pos);
            dict[Encoding.UTF8.GetString(key)] = value;
        }
        pos++; // skip 'e'
        return dict;
    }
}
