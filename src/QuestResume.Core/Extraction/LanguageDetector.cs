namespace QuestResume.Core.Extraction;

/// <summary>
/// Lightweight, dependency-free language detector: counts occurrences of a small set of
/// characteristic stopwords for Portuguese, English, Spanish and French and returns whichever
/// language scores highest. Not meant to compete with a full statistical language-identification
/// model — it's a cheap heuristic good enough to tag/filter documents by predominant language
/// during indexing (see <see cref="Indexing.DocumentIndexer"/>).
/// </summary>
public static class LanguageDetector
{
    /// <summary>ISO 639-1-ish codes used across the app for the languages this detector recognizes.</summary>
    public const string Portuguese = "pt";
    public const string English = "en";
    public const string Spanish = "es";
    public const string French = "fr";
    /// <summary>Returned when the text is too short/has no recognizable stopwords to decide.</summary>
    public const string Unknown = "unknown";

    // Stopwords chosen to be as language-exclusive as possible (e.g. "the"/"and" for English,
    // "não"/"são"/"você" for Portuguese with diacritics that rarely appear in the others).
    private static readonly Dictionary<string, HashSet<string>> StopwordsByLanguage = new(StringComparer.Ordinal)
    {
        [Portuguese] = new(StringComparer.OrdinalIgnoreCase)
        {
            "o", "a", "os", "as", "de", "do", "da", "dos", "das", "que", "não", "são", "para",
            "com", "uma", "um", "por", "mais", "como", "isso", "seu", "sua", "também", "está",
            "você", "muito", "já", "então", "quando", "então", "essa", "esse", "aqui", "ainda",
            "porque", "pelo", "pela", "ção", "há", "só", "até", "entre"
        },
        [English] = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "of", "to", "in", "is", "that", "it", "for", "on", "with", "as", "are",
            "this", "was", "be", "have", "has", "not", "but", "you", "your", "from", "which",
            "were", "their", "about", "there", "when", "what", "who", "will", "would", "into"
        },
        [Spanish] = new(StringComparer.OrdinalIgnoreCase)
        {
            "el", "la", "los", "las", "de", "que", "no", "es", "para", "con", "una", "un", "por",
            "más", "como", "esto", "su", "también", "está", "usted", "mucho", "ya", "entonces",
            "cuando", "esa", "ese", "aquí", "todavía", "porque", "hasta", "entre", "pero"
        },
        [French] = new(StringComparer.OrdinalIgnoreCase)
        {
            "le", "la", "les", "de", "des", "que", "ne", "est", "pour", "avec", "une", "un",
            "par", "plus", "comme", "cela", "son", "sa", "aussi", "être", "vous", "beaucoup",
            "déjà", "alors", "quand", "cette", "ce", "ici", "encore", "parce", "entre", "mais"
        }
    };

    /// <summary>
    /// Returns the detected language code for <paramref name="text"/>, or <see cref="Unknown"/>
    /// if the text is blank or has too few recognizable stopwords to decide confidently.
    /// </summary>
    public static string Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unknown;
        }

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return Unknown;
        }

        var scores = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Portuguese] = 0,
            [English] = 0,
            [Spanish] = 0,
            [French] = 0
        };

        foreach (var rawWord in words)
        {
            // Strip surrounding punctuation but keep accented letters, which are load-bearing for
            // telling Portuguese/Spanish/French apart from English.
            var word = rawWord.Trim(TrimChars);
            if (word.Length == 0) continue;

            foreach (var (language, stopwords) in StopwordsByLanguage)
            {
                if (stopwords.Contains(word))
                {
                    scores[language]++;
                }
            }
        }

        var best = scores.OrderByDescending(kv => kv.Value).First();

        // Require a minimum absolute count so short/ambiguous texts (e.g. a filename-only
        // document) don't get an overconfident language tag from one incidental match.
        const int minimumMatches = 3;
        return best.Value >= minimumMatches ? best.Key : Unknown;
    }

    private static readonly char[] TrimChars =
        { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '«', '»', '-', '\n', '\r', '\t' };
}
