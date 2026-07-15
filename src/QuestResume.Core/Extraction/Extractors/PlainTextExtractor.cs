using QuestResume.Core.Extraction;
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

        // Encoding detection (item 9, ver EncodingDetector): checa BOM primeiro; sem BOM, tenta
        // UTF-8 estrito e cai para Windows-1252/Latin-1 quando os bytes não são UTF-8 válido —
        // evita corromper arquivos legados/exportados em CP1252 (comuns no Windows).
        var text = await EncodingDetector.ReadAllTextDetectedAsync(path, cancellationToken).ConfigureAwait(false);

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
