using System.Text.RegularExpressions;

namespace QuestResume.Core.Rag;

public static class LlmJsonExtractor
{
    private static readonly Regex FencedJsonRegex = new(
        @"```(?:json)?\s*(?<body>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ExtractJsonBlock(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return rawResponse;
        }

        var fenced = FencedJsonRegex.Match(rawResponse);
        if (fenced.Success)
        {
            var body = fenced.Groups["body"].Value.Trim();
            if (body.Length > 0)
            {
                return body;
            }
        }

        var firstArray = rawResponse.IndexOf('[');
        var firstObject = rawResponse.IndexOf('{');

        int start;
        char close;
        if (firstArray >= 0 && (firstObject < 0 || firstArray < firstObject))
        {
            start = firstArray;
            close = ']';
        }
        else if (firstObject >= 0)
        {
            start = firstObject;
            close = '}';
        }
        else
        {
            return rawResponse.Trim();
        }

        var end = rawResponse.LastIndexOf(close);
        if (end < 0 || end < start)
        {
            return rawResponse.Trim();
        }

        return rawResponse[start..(end + 1)].Trim();
    }
}
