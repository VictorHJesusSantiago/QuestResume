using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Splits the text of an <see cref="ExtractedDocument"/> into overlapping chunks small
/// enough to fit comfortably in the LLM context window alongside the prompt and other chunks.
/// </summary>
public static class TextChunker
{
    public static IReadOnlyList<TextChunk> Chunk(ExtractedDocument document, int chunkSize = 1000, int overlap = 150)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize deve ser maior que zero.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "overlap deve estar entre 0 e chunkSize - 1.");
        }

        var text = document.Text.Trim();
        var chunks = new List<TextChunk>();

        if (text.Length == 0)
        {
            return chunks;
        }

        var start = 0;
        var chunkIndex = 0;

        while (start < text.Length)
        {
            var maxEnd = Math.Min(start + chunkSize, text.Length);
            var end = maxEnd < text.Length ? FindBreakPoint(text, start, maxEnd) : maxEnd;

            var chunkText = text[start..end].Trim();
            if (chunkText.Length > 0)
            {
                chunks.Add(new TextChunk
                {
                    SourcePath = document.Path,
                    FileName = document.FileName,
                    ChunkIndex = chunkIndex++,
                    Text = chunkText,
                    ModifiedUtc = document.ModifiedUtc
                });
            }

            if (end >= text.Length)
            {
                break;
            }

            var nextStart = end - overlap;
            start = nextStart > start ? nextStart : end;
        }

        return chunks;
    }

    /// <summary>
    /// Looks backwards from <paramref name="end"/> for a whitespace character so the chunk
    /// doesn't split a word/sentence mid-way, but doesn't search past the midpoint of the
    /// chunk to avoid producing tiny fragments.
    /// </summary>
    private static int FindBreakPoint(string text, int start, int end)
    {
        var minBreak = start + (end - start) / 2;

        for (var i = end; i > minBreak; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
            {
                return i;
            }
        }

        return end;
    }
}
