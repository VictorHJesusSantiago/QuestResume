namespace QuestResume.Core.Extraction;

public static class LanguageDetector
{
        public const string Portuguese = "pt";
    public const string English = "en";
    public const string Spanish = "es";
    public const string French = "fr";
        public const string Unknown = "unknown";

    
    
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

        
        
        const int minimumMatches = 3;
        return best.Value >= minimumMatches ? best.Key : Unknown;
    }

    private static readonly char[] TrimChars =
        { '.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '[', ']', '{', '}', '«', '»', '-', '\n', '\r', '\t' };
}
