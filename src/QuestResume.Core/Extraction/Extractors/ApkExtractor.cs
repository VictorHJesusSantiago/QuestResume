using System.IO.Compression;
using System.Text;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

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
