using System.Text;
using System.Text.RegularExpressions;
using QuestResume.Core.Models;

namespace QuestResume.Core.Indexing;

public static class TextChunker
{
        public static readonly IReadOnlyCollection<string> CodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".py", ".java", ".ts", ".tsx", ".jsx", ".js", ".go", ".rb", ".php",
        ".c", ".cpp", ".h", ".hpp", ".rs", ".kt", ".swift"
    };

        private static readonly Regex CodeBoundary = new(
        @"^\s*(?:(?:public|private|protected|internal|static|async|export|default|abstract|override|virtual|readonly|final|sealed|pub)\s+)*(?:class|interface|struct|enum|record|trait|impl|namespace|module|def|func|fn|function)\b",
        RegexOptions.Compiled);

        private const char PageMarker = '\f';

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

        private static readonly Regex SentenceBoundary = new(@"(?<=[.!?])\s+", RegexOptions.Compiled);

        public static IReadOnlyList<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return SentenceBoundary.Split(text.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

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

        private static readonly Regex MarkdownHeading = new(@"^(#{1,6})\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex HtmlHeading = new(@"<h([1-6])[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static readonly IReadOnlyCollection<string> HeadingAwareExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown", ".html", ".htm"
    };

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
