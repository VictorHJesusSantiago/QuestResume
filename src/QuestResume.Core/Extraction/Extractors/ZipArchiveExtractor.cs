using System.IO.Compression;
using System.Text;
using QuestResume.Core.Configuration;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from .zip archives by running every supported entry through the standard
/// extractor registry and concatenating the results, each prefixed with the entry's path
/// inside the archive. Nested archives are skipped (the inner registry is built with
/// <c>includeArchives: false</c>) to avoid unbounded recursion on zip bombs/zip-of-zips.
/// </summary>
public sealed class ZipArchiveExtractor : IFileExtractor
{
    private readonly ExtractorRegistry _innerRegistry;

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".zip" };

    public ZipArchiveExtractor(AppOptions? options = null)
    {
        _innerRegistry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options, includeArchives: false));
    }

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        using (var archive = ZipFile.OpenRead(path))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Directory entries have an empty Name (only FullName ends with '/').
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var extension = Path.GetExtension(entry.Name);
                if (!_innerRegistry.IsSupported(extension))
                {
                    continue;
                }

                var tempFile = Path.Combine(Path.GetTempPath(), $"qr_zip_{Guid.NewGuid()}{extension}");
                try
                {
                    entry.ExtractToFile(tempFile, overwrite: true);
                    var entryDocument = await _innerRegistry.ExtractAsync(tempFile, cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(entryDocument.Text))
                    {
                        builder.AppendLine($"--- {entry.FullName} ---");
                        builder.AppendLine(entryDocument.Text);
                        builder.AppendLine();
                    }

                    if (entryDocument.Metadata.TryGetValue("warning", out var warning))
                    {
                        warnings.Add($"{entry.FullName}: {warning}");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"{entry.FullName}: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
            }
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

        return document;
    }
}
