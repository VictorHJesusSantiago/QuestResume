using System.IO.Compression;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Best-effort extractor for Apple iWork documents (.pages, .numbers, .key). These are zip
/// packages whose actual document body is stored in Apple's proprietary IWA (Snappy-compressed
/// protobuf) format, which this project does not parse. Most iWork files, however, bundle a
/// human-readable rendition at <c>QuickLook/Preview.pdf</c> (or <c>preview.pdf</c>) used by
/// macOS Quick Look — when present, that PDF is extracted via <see cref="PdfExtractor"/>, giving
/// full text for most real-world documents. When no preview is present, the file is still
/// indexed (by name) with a PT-BR warning in <see cref="ExtractedDocument.Metadata"/>, following
/// the project's graceful-degradation convention rather than throwing.
/// </summary>
public sealed class IWorkExtractor : IFileExtractor
{
    private static readonly string[] PreviewEntryNames =
    {
        "QuickLook/Preview.pdf",
        "quicklook/preview.pdf",
        "preview.pdf",
        "Preview.pdf"
    };

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".pages", ".numbers", ".key" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        ZipArchiveEntry? previewEntry = null;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            previewEntry = PreviewEntryNames
                .Select(name => archive.GetEntry(name))
                .FirstOrDefault(e => e is not null)
                ?? archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("preview.pdf", StringComparison.OrdinalIgnoreCase));

            if (previewEntry is not null)
            {
                var tempPdfPath = Path.Combine(Path.GetTempPath(), $"questresume_iwork_{Guid.NewGuid():N}.pdf");
                try
                {
                    await using (var entryStream = previewEntry.Open())
                    await using (var fileStream = File.Create(tempPdfPath))
                    {
                        await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                    }

                    var pdfExtractor = new PdfExtractor();
                    var pdfDocument = await pdfExtractor.ExtractAsync(tempPdfPath, cancellationToken).ConfigureAwait(false);

                    return new ExtractedDocument
                    {
                        Path = path,
                        FileName = info.Name,
                        Extension = info.Extension,
                        Text = pdfDocument.Text,
                        ModifiedUtc = info.LastWriteTimeUtc,
                        Metadata = new Dictionary<string, string>
                        {
                            ["warning"] = "Texto extraído da pré-visualização (Quick Look) do documento iWork; formatação e conteúdo detalhado podem não estar completos."
                        }
                    };
                }
                finally
                {
                    if (File.Exists(tempPdfPath))
                    {
                        File.Delete(tempPdfPath);
                    }
                }
            }
        }
        catch (InvalidDataException)
        {
            // Not a valid zip; fall through to the "no content extracted" result below.
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = string.Empty,
            ModifiedUtc = info.LastWriteTimeUtc,
            Metadata = new Dictionary<string, string>
            {
                ["warning"] = "Suporte parcial a arquivos iWork: nenhuma pré-visualização (Quick Look) encontrada neste arquivo, apenas o nome foi indexado."
            }
        };
    }
}
