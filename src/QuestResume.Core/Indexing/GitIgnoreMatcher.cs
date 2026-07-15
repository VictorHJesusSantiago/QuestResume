using System.Text.RegularExpressions;

namespace QuestResume.Core.Indexing;

/// <summary>
/// Simple ".gitignore"-style glob matcher (item 10) used to apply an optional
/// <c>.questresumeignore</c> file at the root of the indexed folder. Supports a practical subset
/// of gitignore syntax: blank lines and <c>#</c> comments are ignored; <c>!pattern</c> negates a
/// previous match (last matching rule wins, same as real gitignore); a leading <c>/</c> anchors
/// the pattern to the root folder instead of matching anywhere; <c>*</c> matches any run of
/// characters except <c>/</c>; <c>**</c> matches across directory separators; <c>?</c> matches a
/// single character except <c>/</c>. Not a full gitignore implementation (no character classes,
/// no directory-only trailing-slash distinction beyond plain matching).
/// </summary>
public sealed class GitIgnoreMatcher
{
    private sealed record Rule(Regex Regex, bool Negate);

    private readonly List<Rule> _rules;

    private GitIgnoreMatcher(List<Rule> rules)
    {
        _rules = rules;
    }

    /// <summary>Parses the given lines (already split, as read from a <c>.questresumeignore</c> file) into a matcher.</summary>
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

            // A trailing slash denotes "directory only" in real gitignore; treated here simply as
            // "matches this path and anything under it" by stripping the slash before compiling.
            line = line.TrimEnd('/');

            var regex = CompileGlob(line, anchored);
            rules.Add(new Rule(regex, negate));
        }

        return new GitIgnoreMatcher(rules);
    }

    /// <summary>Loads and parses <c>.questresumeignore</c> from the given root folder, or returns an empty (no-op) matcher if it doesn't exist.</summary>
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

    /// <summary>
    /// Returns true if <paramref name="relativePath"/> (forward-slash separated, relative to the
    /// ignore file's root folder) is excluded — i.e., the last matching rule was not a negation.
    /// </summary>
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
            // Unanchored: the pattern may match starting at any path segment, e.g. "tmp/*"
            // (without a leading slash) matches "a/tmp/file" as well as "tmp/file".
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
                    // "**" matches any number of path segments (including zero), across "/".
                    sb.Append(".*");
                    i += 2;
                    // Skip an immediately following "/" so "**/tmp" doesn't require a leading slash.
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
