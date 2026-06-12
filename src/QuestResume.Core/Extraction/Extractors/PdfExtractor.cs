using System.Text;
using QuestResume.Core.Models;
using UglyToad.PdfPig;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts text from PDF and PDF/A documents using PdfPig. Scanned PDFs with no embedded
/// text will simply yield an empty string here (OCR is handled by a separate Group 3 extractor).
/// </summary>
public sealed class PdfExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".pdf" };

    public Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var builder = new StringBuilder();

        using (var document = PdfDocument.Open(path))
        {
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.AppendLine(page.Text);
            }
        }

        return Task.FromResult(new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = builder.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc
        });
    }
}
