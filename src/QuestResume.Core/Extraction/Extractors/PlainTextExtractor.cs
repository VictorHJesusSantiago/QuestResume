using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Reads files that are already plain text (or text-ish formats whose raw content is
/// useful enough for search/Q&amp;A without further parsing).
/// </summary>
public sealed class PlainTextExtractor : IFileExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[]
    {
        ".txt", ".csv", ".css", ".js", ".json", ".xml", ".bib", ".tex", ".ics", ".vcf",
        // Source code / config formats: read as plain text and chunked function/class-aware
        // by TextChunker.ChunkCode (see TextChunker.CodeExtensions).
        ".cs", ".py", ".java", ".ts", ".tsx", ".jsx", ".go", ".rb", ".php", ".c", ".cpp",
        ".h", ".hpp", ".rs", ".kt", ".swift", ".sh", ".ps1", ".sql", ".yaml", ".yml", ".md"
    };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);

        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

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
