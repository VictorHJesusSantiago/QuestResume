using System.Text;
using QuestResume.Core.Configuration;
using QuestResume.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class ArchiveExtractor : IFileExtractor
{
    private readonly ExtractorRegistry _innerRegistry;

    
    
    
    
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".7z", ".rar", ".gz", ".tgz" };

    public ArchiveExtractor(AppOptions? options = null)
    {
        _innerRegistry = new ExtractorRegistry(ExtractorRegistry.DefaultExtractors(options, includeArchives: false));
    }

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();
        var warnings = new List<string>();

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = ReaderFactory.OpenReader(stream);

            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (reader.Entry.IsDirectory)
                {
                    continue;
                }

                var entryName = reader.Entry.Key ?? string.Empty;
                var extension = Path.GetExtension(entryName);
                if (!_innerRegistry.IsSupported(extension))
                {
                    continue;
                }

                var tempFile = Path.Combine(Path.GetTempPath(), $"qr_archive_{Guid.NewGuid()}{extension}");
                try
                {
                    using (var entryStream = reader.OpenEntryStream())
                    using (var outStream = File.Create(tempFile))
                    {
                        entryStream.CopyTo(outStream);
                    }

                    var entryDocument = await _innerRegistry.ExtractAsync(tempFile, cancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(entryDocument.Text))
                    {
                        builder.AppendLine($"--- {entryName} ---");
                        builder.AppendLine(entryDocument.Text);
                        builder.AppendLine();
                    }

                    if (entryDocument.Metadata.TryGetValue("warning", out var warning))
                    {
                        warnings.Add($"{entryName}: {warning}");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"{entryName}: {ex.Message}");
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
        catch (Exception ex)
        {
            warnings.Add($"Falha ao abrir arquivo compactado ({info.Extension}): {ex.Message}");
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
