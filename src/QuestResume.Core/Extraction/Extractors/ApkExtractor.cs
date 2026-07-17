using System.IO.Compression;
using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts a best-effort summary of an Android .apk package. An APK is a ZIP archive, so the
/// entry list is always readable; <c>AndroidManifest.xml</c> inside it is stored as Android's
/// compact binary XML (AXML) format, not plain XML. This extractor lists the ZIP contents and
/// attempts a minimal AXML string-pool scan (extracting readable UTF-16 strings from the
/// manifest's string pool chunk) to surface package/permission-like strings on a best-effort
/// basis; it does not fully parse the AXML tree. If the manifest can't be read or decoded
/// meaningfully, falls back to just the ZIP entry listing with a clear warning.
/// </summary>
public sealed class ApkExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".apk" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        try
        {
            using var archive = ZipFile.OpenRead(path);

            builder.AppendLine("## Conteúdo do pacote APK");
            foreach (var entry in archive.Entries.Take(500))
            {
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    builder.AppendLine($"- {entry.FullName} ({entry.Length} bytes)");
                }
            }

            var manifestEntry = archive.GetEntry("AndroidManifest.xml");
            if (manifestEntry is not null)
            {
                try
                {
                    using var stream = manifestEntry.Open();
                    using var memory = new MemoryStream();
                    stream.CopyTo(memory);
                    var strings = ExtractAxmlStrings(memory.ToArray());
                    if (strings.Count > 0)
                    {
                        builder.AppendLine();
                        builder.AppendLine("## Strings extraídas do AndroidManifest.xml (binário AXML, extração best-effort do pool de strings)");
                        foreach (var s in strings.Take(200))
                        {
                            builder.AppendLine($"- {s}");
                        }
                    }
                    else
                    {
                        warnings.Add("AndroidManifest.xml presente, mas nenhuma string legível foi extraída do pool binário AXML.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Falha ao ler AndroidManifest.xml (formato binário AXML, suporte parcial): {ex.Message}");
                }
            }
            else
            {
                warnings.Add("AndroidManifest.xml não encontrado no pacote.");
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Falha ao abrir o pacote APK como ZIP: {ex.Message}");
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

    /// <summary>
    /// Minimal AXML string-pool scan: Android's binary XML format begins with a
    /// ResStringPool_header chunk holding UTF-16LE (or UTF-8) length-prefixed strings. Rather
    /// than fully parsing the chunk headers (out of scope for best-effort text extraction), this
    /// scans the raw bytes for runs that look like plausible UTF-16LE printable text, which in
    /// practice recovers most package names, permission strings, and activity/class names.
    /// </summary>
    private static List<string> ExtractAxmlStrings(byte[] bytes)
    {
        var results = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var ch = (char)(bytes[i] | (bytes[i + 1] << 8));
            if (ch >= 0x20 && ch < 0x7F)
            {
                current.Append(ch);
            }
            else
            {
                if (current.Length >= 4)
                {
                    results.Add(current.ToString());
                }
                current.Clear();
            }
        }

        if (current.Length >= 4)
        {
            results.Add(current.ToString());
        }

        return results.Distinct().ToList();
    }
}
