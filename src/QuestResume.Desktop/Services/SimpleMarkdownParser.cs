using System.Text;
using System.Windows;
using System.Windows.Documents;

namespace QuestResume.Desktop.Services;

public static class SimpleMarkdownParser
{
        public static IEnumerable<Inline> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var i = 0;
        var plain = new StringBuilder();

        void FlushPlain(ICollection<Inline> into)
        {
            if (plain.Length > 0)
            {
                into.Add(new Run(plain.ToString()));
                plain.Clear();
            }
        }

        var result = new List<Inline>();

        while (i < text.Length)
        {
            
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i)
                {
                    FlushPlain(result);
                    result.Add(new Bold(new Run(text.Substring(i + 2, end - i - 2))));
                    i = end + 2;
                    continue;
                }
            }

            
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i)
                {
                    FlushPlain(result);
                    var code = new Run(text.Substring(i + 1, end - i - 1))
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        Background = System.Windows.Media.Brushes.WhiteSmoke
                    };
                    result.Add(code);
                    i = end + 1;
                    continue;
                }
            }

            
            if (text[i] == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i)
                {
                    FlushPlain(result);
                    result.Add(new Italic(new Run(text.Substring(i + 1, end - i - 1))));
                    i = end + 1;
                    continue;
                }
            }

            plain.Append(text[i]);
            i++;
        }

        FlushPlain(result);

        foreach (var inline in result)
        {
            yield return inline;
        }
    }
}
