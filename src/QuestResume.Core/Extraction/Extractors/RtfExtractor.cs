using QuestResume.Core.Models;
using RtfPipe;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts plain text from Rich Text Format (.rtf) files using RtfPipe.
/// </summary>
public sealed class RtfExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".rtf" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var rtf = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var html = Rtf.ToHtml(rtf);
        var text = HtmlExtractor.ExtractVisibleText(html);

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = text,
            ModifiedUtc = info.LastWriteTimeUtc
        };
    }
}
