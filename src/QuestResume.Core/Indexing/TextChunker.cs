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

    /// <summary>
    /// Form-feed character used by <see cref="QuestResume.Core.Extraction.Extractors.PdfExtractor"/>
    /// to mark page boundaries in the extracted text before chunking. Stripped out of the text
    /// used for chunking/searching — only used here to compute <see cref="TextChunk.PageNumber"/>.
    /// </summary>
    private const char PageMarker = '\f';

    /// <summary>
    /// Removes every <see cref="PageMarker"/> from <paramref name="text"/> and returns the
    /// cleaned text alongside a list of <c>(Offset, Page)</c> pairs (sorted by <c>Offset</c>,
    /// ascending) recording, for the cleaned text, the character offset where each page begins
    /// (1-based page numbers). Returns an empty list when <paramref name="text"/> has no markers
    /// at all, signalling the source document has no page information.
    /// </summary>
    private static (string CleanText, List<(int Offset, int Page)> PageBreaks) StripPageMarkers(string text)
    {
        if (text.IndexOf(PageMarker) < 0)
        {
            return (text, new List<(int, int)>());
        }

        var sb = new StringBuilder(text.Length);
        var breaks = new List<(int Offset, int Page)> { (0, 1) };
        var page = 1;

        foreach (var ch in text)
        {
            if (ch == PageMarker)
            {
                page++;
                breaks.Add((sb.Length, page));
                continue;
            }

            sb.Append(ch);
        }

        return (sb.ToString(), breaks);
    }

    /// <summary>
    /// Returns the page number of the last entry in <paramref name="pageBreaks"/> whose
    /// <c>Offset</c> is at or before <paramref name="position"/>, or <c>null</c> when
    /// <paramref name="pageBreaks"/> is empty (source has no page markers).
    /// </summary>
    private static int? GetPageNumber(List<(int Offset, int Page)> pageBreaks, int position)
    {
        if (pageBreaks.Count == 0)
        {
            return null;
        }

        var page = pageBreaks[0].Page;
        foreach (var (offset, pageNumber) in pageBreaks)
        {
            if (offset > position)
            {
                break;
            }

            page = pageNumber;
        }

        return page;
    }

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

        var (cleanedText, pageBreaks) = StripPageMarkers(document.Text);

        var leadingTrim = cleanedText.Length - cleanedText.TrimStart().Length;
        var text = cleanedText.Trim();
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
                    ModifiedUtc = document.ModifiedUtc,
                    PageNumber = GetPageNumber(pageBreaks, start + leadingTrim)
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

    /// <summary>
    /// Matches sentence-ending punctuation followed by whitespace, used to split text into
    /// individual sentences for <see cref="ChunkBySentences"/> and semantic chunking. Deliberately
    /// simple (no abbreviation handling) — good enough for retrieval purposes where an occasional
    /// over-split sentence is harmless.
    /// </summary>
    private static readonly Regex SentenceBoundary = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    /// <summary>
    /// Splits <paramref name="text"/> into non-empty, trimmed sentences using
    /// <see cref="SentenceBoundary"/>.
    /// </summary>
    public static IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return SentenceBoundary.Split(text.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Sentence-window chunking (<see cref="Configuration.AppOptions.SentenceWindowChunkingEnabled"/>):
    /// each chunk is a single sentence, indexed/embedded individually for precise matching.
    /// The surrounding context (<c>SentenceWindowSize</c> sentences before/after) is reconstructed
    /// at retrieval time by <see cref="SearchService.ExpandSentenceWindow"/> from sibling chunks
    /// of the same document, rather than stored redundantly in every chunk.
    /// Sentences longer than <paramref name="chunkSize"/> are further split by the regular
    /// sliding-window <see cref="Chunk"/>.
    /// </summary>
    public static IReadOnlyList<TextChunk> ChunkBySentences(ExtractedDocument document, int chunkSize = 1000)
    {
        var sentences = SplitSentences(document.Text);
        if (sentences.Count == 0) return Array.Empty<TextChunk>();

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (sentence.Length <= chunkSize)
            {
                chunks.Add(new TextChunk
                {
                    SourcePath = document.Path,
                    FileName = document.FileName,
                    ChunkIndex = chunkIndex++,
                    Text = sentence,
                    ModifiedUtc = document.ModifiedUtc
                });
                continue;
            }

            var subDocument = new ExtractedDocument
            {
                Path = document.Path,
                FileName = document.FileName,
                Extension = document.Extension,
                Text = sentence,
                ModifiedUtc = document.ModifiedUtc
            };

            foreach (var subChunk in Chunk(subDocument, chunkSize, overlap: 0))
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

        return chunks;
    }

    /// <summary>
    /// Matches a Markdown ATX heading (<c>#</c> .. <c>######</c>) at the start of a line, or an
    /// HTML <c>&lt;h1&gt;</c>-<c>&lt;h6&gt;</c> opening tag, used by <see cref="ChunkByHeadings"/>.
    /// </summary>
    private static readonly Regex MarkdownHeading = new(@"^(#{1,6})\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex HtmlHeading = new(@"<h([1-6])[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>File extensions eligible for heading-aware chunking (<see cref="ChunkByHeadings"/>).</summary>
    public static readonly IReadOnlyCollection<string> HeadingAwareExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".html", ".htm"
    };

    /// <summary>
    /// Heading-hierarchy chunking (<see cref="Configuration.AppOptions.HeadingAwareChunkingEnabled"/>):
    /// for Markdown/HTML documents, splits the text at heading boundaries (<c>#</c>-<c>######</c>
    /// or <c>&lt;h1&gt;</c>-<c>&lt;h6&gt;</c>) so each chunk stays within a single section. Sections
    /// still bigger than <paramref name="chunkSize"/> are further subdivided by the regular
    /// sliding-window <see cref="Chunk"/>.
    /// </summary>
    public static IReadOnlyList<TextChunk> ChunkByHeadings(ExtractedDocument document, int chunkSize = 1000, int overlap = 150)
    {
        var text = document.Text;
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<TextChunk>();

        var isHtml = document.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase)
            || document.Extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);

        var boundaries = new List<int>();
        if (isHtml)
        {
            foreach (Match m in HtmlHeading.Matches(text)) boundaries.Add(m.Index);
        }
        else
        {
            foreach (Match m in MarkdownHeading.Matches(text)) boundaries.Add(m.Index);
        }

        var sections = new List<string>();
        if (boundaries.Count == 0)
        {
            sections.Add(text);
        }
        else
        {
            if (boundaries[0] > 0)
            {
                sections.Add(text[..boundaries[0]]);
            }

            for (var i = 0; i < boundaries.Count; i++)
            {
                var start = boundaries[i];
                var end = i + 1 < boundaries.Count ? boundaries[i + 1] : text.Length;
                sections.Add(text[start..end]);
            }
        }

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        foreach (var section in sections)
        {
            var trimmed = section.Trim();
            if (trimmed.Length == 0) continue;

            if (trimmed.Length <= chunkSize)
            {
                chunks.Add(new TextChunk
                {
                    SourcePath = document.Path,
                    FileName = document.FileName,
                    ChunkIndex = chunkIndex++,
                    Text = trimmed,
                    ModifiedUtc = document.ModifiedUtc
                });
                continue;
            }

            var subDocument = new ExtractedDocument
            {
                Path = document.Path,
                FileName = document.FileName,
                Extension = document.Extension,
                Text = trimmed,
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

        return chunks;
    }

    /// <summary>
    /// Parent-child chunking (<see cref="Configuration.AppOptions.ParentChildChunkingEnabled"/>):
    /// first splits the document into non-overlapping "parent" windows of
    /// <paramref name="parentChunkSize"/> characters, then splits each parent window further into
    /// "child" windows of <paramref name="childChunkSize"/> characters. Returns the child chunks
    /// (used for search/embedding), each carrying its parent's full text in
    /// <see cref="TextChunk.ParentText"/> for the caller to substitute back in at retrieval time
    /// (see <see cref="SearchService.Search"/>).
    /// </summary>
    public static IReadOnlyList<TextChunk> ChunkParentChild(ExtractedDocument document, int parentChunkSize = 1500, int childChunkSize = 200)
    {
        var text = document.Text.Trim();
        if (text.Length == 0) return Array.Empty<TextChunk>();

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;
        var parentStart = 0;

        while (parentStart < text.Length)
        {
            var parentMaxEnd = Math.Min(parentStart + parentChunkSize, text.Length);
            var parentEnd = parentMaxEnd < text.Length ? FindBreakPoint(text, parentStart, parentMaxEnd) : parentMaxEnd;
            var parentText = text[parentStart..parentEnd].Trim();

            if (parentText.Length > 0)
            {
                var childStart = 0;

                while (childStart < parentText.Length)
                {
                    var childMaxEnd = Math.Min(childStart + childChunkSize, parentText.Length);
                    var childEnd = childMaxEnd < parentText.Length ? FindBreakPoint(parentText, childStart, childMaxEnd) : childMaxEnd;
                    var childText = parentText[childStart..childEnd].Trim();

                    if (childText.Length > 0)
                    {
                        chunks.Add(new TextChunk
                        {
                            SourcePath = document.Path,
                            FileName = document.FileName,
                            ChunkIndex = chunkIndex++,
                            Text = childText,
                            ModifiedUtc = document.ModifiedUtc,
                            ParentText = parentText
                        });
                    }

                    childStart = childEnd > childStart ? childEnd : childMaxEnd;
                }
            }

            parentStart = parentEnd > parentStart ? parentEnd : parentMaxEnd;
        }

        return chunks;
    }
}
