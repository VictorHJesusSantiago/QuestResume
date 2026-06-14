using System.Text;
using System.Text.RegularExpressions;
using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Splits the text of an <see cref="ExtractedDocument"/> into overlapping chunks small
/// enough to fit comfortably in the LLM context window alongside the prompt and other chunks.
/// </summary>
public static class TextChunker
{
    /// <summary>
    /// File extensions that get function/class-aware chunking via <see cref="ChunkCode"/>
    /// instead of the plain sliding-window <see cref="Chunk"/>.
    /// </summary>
    public static readonly IReadOnlyCollection<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".java", ".ts", ".tsx", ".jsx", ".js", ".go", ".rb", ".php",
        ".c", ".cpp", ".h", ".hpp", ".rs", ".kt", ".swift"
    };

    /// <summary>
    /// Matches lines that commonly start a top-level declaration (class/interface/struct/
    /// function/method) across mainstream languages. Used as a forced chunk boundary so a
    /// declaration's body stays together with its signature whenever it fits in
    /// <c>chunkSize</c>.
    /// </summary>
    private static readonly Regex CodeBoundary = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|export|default|abstract|override|virtual|readonly|final|sealed|pub)\s+)*(?:class|interface|struct|enum|record|trait|impl|namespace|module|def|func|fn|function)\b",
        RegexOptions.Compiled);

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

    /// <summary>
    /// Chunks source code by grouping lines into blocks that start at a top-level declaration
    /// (<see cref="CodeBoundary"/>), so a function/class body stays in one chunk whenever it
    /// fits. Blocks are then greedily merged up to <paramref name="chunkSize"/>, and any block
    /// still too big is split with the regular sliding-window <see cref="Chunk"/>.
    /// </summary>
    public static IReadOnlyList<TextChunk> ChunkCode(ExtractedDocument document, int chunkSize = 1000, int overlap = 150)
    {
        var text = document.Text.Trim();
        if (text.Length == 0)
        {
            return Array.Empty<TextChunk>();
        }

        var blocks = new List<string>();
        var currentBlock = new StringBuilder();

        foreach (var line in text.Split('\n'))
        {
            if (CodeBoundary.IsMatch(line) && currentBlock.Length > 0)
            {
                blocks.Add(currentBlock.ToString());
                currentBlock.Clear();
            }

            currentBlock.AppendLine(line);
        }

        if (currentBlock.Length > 0)
        {
            blocks.Add(currentBlock.ToString());
        }

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;
        var pending = new StringBuilder();

        void FlushPending()
        {
            var pendingText = pending.ToString().Trim();
            pending.Clear();

            if (pendingText.Length == 0)
            {
                return;
            }

            if (pendingText.Length <= chunkSize)
            {
                chunks.Add(new TextChunk
                {
                    SourcePath = document.Path,
                    FileName = document.FileName,
                    ChunkIndex = chunkIndex++,
                    Text = pendingText,
                    ModifiedUtc = document.ModifiedUtc
                });
                return;
            }

            var subDocument = new ExtractedDocument
            {
                Path = document.Path,
                FileName = document.FileName,
                Extension = document.Extension,
                Text = pendingText,
                ModifiedUtc = document.ModifiedUtc
            };

            foreach (var subChunk in Chunk(subDocument, chunkSize, overlap))
            {
                chunks.Add(new TextChunk
                {
                    SourcePath = subChunk.SourcePath,
                    FileName = subChunk.FileName,
                    ChunkIndex = chunkIndex++,
                    Text = subChunk.Text,
                    ModifiedUtc = subChunk.ModifiedUtc
                });
            }
        }

        foreach (var block in blocks)
        {
            if (pending.Length > 0 && pending.Length + block.Length > chunkSize)
            {
                FlushPending();
            }

            pending.Append(block);
        }

        FlushPending();

        return chunks;
    }
}
