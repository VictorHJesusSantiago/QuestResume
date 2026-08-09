using System.Text;
using System.Text.RegularExpressions;

namespace QuestResume.Core.Extraction.Extractors;

public static class TableHeuristics
{
    private const int MinTableRows = 2;

    
    private static readonly Regex ColumnSplitter = new(@"\t|  +", RegexOptions.Compiled);

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
