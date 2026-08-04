using System.Text;
using QuestResume.Core.Models;
using VersOne.Epub;

namespace QuestResume.Core.Extraction.Extractors;

public sealed class EpubExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".epub" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var book = await EpubReader.ReadBookAsync(path).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine(book.Title);

        foreach (var chapter in book.ReadingOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine(HtmlExtractor.ExtractVisibleText(chapter.Content));
        }

        return new ExtractedDocument
        {
            Path = path,
            FileName = info.Name,
            Extension = info.Extension,
            Text = builder.ToString(),
            ModifiedUtc = info.LastWriteTimeUtc
        };
    }
}
