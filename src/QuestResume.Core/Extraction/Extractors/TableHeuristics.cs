using System.Text;
using System.Text.RegularExpressions;

namespace QuestResume.Core.Extraction.Extractors;

/// <summary>
/// Item 16: heuristic detection of grid-like tables inside plain extracted PDF text, formatting
/// detected blocks as Markdown tables instead of leaving them as flattened, space-separated
/// lines. Purely text-based (column boundaries are inferred from runs of 2+ spaces or a tab
/// character) — this is a heuristic, not a real layout analysis, so it can miss tables whose
/// cell text wraps across lines, tables with single/borderless columns that don't leave a wide
/// gap between "columns", or misfire on aligned-but-unrelated text (e.g. a table of contents).
/// It only kicks in when at least <see cref="MinTableRows"/> consecutive lines all split into
/// the same number (&gt;= 2) of columns, which keeps false positives rare in practice.
/// </summary>
public static class TableHeuristics
{
    private const int MinTableRows = 2;

    // 2+ spaces or a tab character = column separator.
    private static readonly Regex ColumnSplitter = new(@"\t|  +", RegexOptions.Compiled);

    /// <summary>
    /// Scans <paramref name="pageText"/> line by line and rewrites any run of
    /// <see cref="MinTableRows"/>+ consecutive lines that all split into the same number of
    /// columns (&gt;= 2) as a Markdown table (<c>| col1 | col2 |</c> with a
    /// <c>|---|---|</c> separator after the first row, treated as the header). Lines outside
    /// such a run are left untouched.
    /// </summary>
    public static string DetectAndFormatTables(string pageText)
    {
        if (string.IsNullOrEmpty(pageText))
        {
            return pageText;
        }

        var lines = pageText.Split('\n');
        var output = new StringBuilder();

        var i = 0;
        while (i < lines.Length)
        {
            var columns = SplitColumns(lines[i]);
            if (columns is null)
            {
                output.Append(lines[i]);
                if (i < lines.Length - 1) output.Append('\n');
                i++;
                continue;
            }

            var runStart = i;
            var runColumnCount = columns.Count;
            var rows = new List<IReadOnlyList<string>> { columns };

            var j = i + 1;
            while (j < lines.Length)
            {
                var nextColumns = SplitColumns(lines[j]);
                if (nextColumns is null || nextColumns.Count != runColumnCount)
                {
                    break;
                }

                rows.Add(nextColumns);
                j++;
            }

            if (rows.Count >= MinTableRows)
            {
                AppendMarkdownTable(output, rows);
                if (j < lines.Length) output.Append('\n');
                i = j;
            }
            else
            {
                // Not enough consecutive matching rows to call it a table — emit the original
                // line(s) unchanged and move forward by one.
                output.Append(lines[runStart]);
                if (runStart < lines.Length - 1) output.Append('\n');
                i = runStart + 1;
            }
        }

        return output.ToString();
    }

    private static List<string>? SplitColumns(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = ColumnSplitter.Split(line.TrimEnd('\r')).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return parts.Count >= 2 ? parts : null;
    }

    private static void AppendMarkdownTable(StringBuilder output, List<IReadOnlyList<string>> rows)
    {
        var columnCount = rows[0].Count;

        output.Append('|').Append(string.Join('|', rows[0].Select(c => $" {c} "))).Append('|').Append('\n');
        output.Append('|').Append(string.Join('|', Enumerable.Repeat("---", columnCount))).Append('|').Append('\n');

        for (var r = 1; r < rows.Count; r++)
        {
            output.Append('|').Append(string.Join('|', rows[r].Select(c => $" {c} "))).Append('|');
            if (r < rows.Count - 1) output.Append('\n');
        }
    }
}
