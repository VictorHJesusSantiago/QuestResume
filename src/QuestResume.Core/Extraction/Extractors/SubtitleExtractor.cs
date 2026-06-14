using System.Text;
using System.Text.RegularExpressions;
using QuestResume.Core.Models;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Extracts the spoken/displayed text from subtitle files (.srt, .vtt), discarding cue
/// sequence numbers, timestamps and markup so only the dialogue remains searchable.
/// </summary>
public sealed class SubtitleExtractor : IFileExtractor
{
    private static readonly Regex SequenceNumberLine = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex TimestampLine = new(@"-->", RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    public IReadOnlyCollection<string> SupportedExtensions { get; } = new[] { ".srt", ".vtt" };

    public async Task<ExtractedDocument> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(path);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        string? lastLine = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.Length == 0
                || string.Equals(line, "WEBVTT", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)
                || SequenceNumberLine.IsMatch(line)
                || TimestampLine.IsMatch(line))
            {
                continue;
            }

            var text = HtmlTag.Replace(line, string.Empty).Trim();

            // Subtitle cues frequently repeat the same line across consecutive timestamps
            // (e.g. karaoke-style captions); skip immediate duplicates to avoid noise.
            if (text.Length > 0 && !string.Equals(text, lastLine, StringComparison.Ordinal))
            {
                builder.AppendLine(text);
                lastLine = text;
            }
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
