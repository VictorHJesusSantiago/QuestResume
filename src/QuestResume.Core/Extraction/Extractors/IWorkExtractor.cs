using System.IO.Compression;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

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
