using System.Text.RegularExpressions;

namespace QuestResume.Core.Indexing;

public sealed class GitIgnoreMatcher
{
    private sealed record Rule(Regex Regex, bool Negate);

    private readonly List<Rule> _rules;

    private GitIgnoreMatcher(List<Rule> rules)
    {
        _rules = rules;
    }

        public static GitIgnoreMatcher Parse(IEnumerable<string> lines)
    {
        var rules = new List<Rule>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var negate = false;
            if (line.StartsWith('!'))
            {
                negate = true;
                line = line[1..];
            }

            if (line.Length == 0)
            {
                continue;
            }

            var anchored = line.StartsWith('/');
            if (anchored)
            {
                line = line[1..];
            }

            
            
            line = line.TrimEnd('/');

            var regex = CompileGlob(line, anchored);
            rules.Add(new Rule(regex, negate));
        }

        return new GitIgnoreMatcher(rules);
    }

        public static GitIgnoreMatcher LoadFromFolder(string folderPath)
    {
        var ignoreFilePath = Path.Combine(folderPath, ".questresumeignore");
        if (!File.Exists(ignoreFilePath))
        {
            return new GitIgnoreMatcher(new List<Rule>());
        }

        return Parse(File.ReadAllLines(ignoreFilePath));
    }

    public bool HasRules => _rules.Count > 0;

        public bool IsIgnored(string relativePath)
    {
        if (_rules.Count == 0) return false;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        var ignored = false;

        foreach (var rule in _rules)
        {
            if (rule.Regex.IsMatch(normalized))
            {
                ignored = !rule.Negate;
            }
        }

        return ignored;
    }

    private static Regex CompileGlob(string pattern, bool anchored)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('^');
        if (!anchored)
        {
            
            
            sb.Append("(?:.*/)?");
        }

        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    
                    sb.Append(".*");
                    i += 2;
                    
                    if (i < pattern.Length && pattern[i] == '/')
                    {
                        i++;
                    }
                    continue;
                }

                sb.Append("[^/]*");
                i++;
                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                i++;
                continue;
            }

            sb.Append(Regex.Escape(c.ToString()));
            i++;
        }

        sb.Append("(?:/.*)?$");
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
