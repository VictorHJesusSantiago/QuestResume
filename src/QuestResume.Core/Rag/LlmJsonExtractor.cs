using System.Text.RegularExpressions;

namespace QuestResume.Core.Rag;

/// <summary>
/// Extrai defensivamente um bloco JSON (array ou objeto) de dentro de uma resposta de LLM que
/// pode conter texto extra (explicações, cercas de código markdown, etc.), usado por
/// <see cref="StructuredExtractionService"/> e <see cref="FlashcardService"/> antes de
/// desserializar a resposta.
/// </summary>
public static class LlmJsonExtractor
{
    private static readonly Regex FencedJsonRegex = new(
        @"```(?:json)?\s*(?<body>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Tenta isolar o conteúdo JSON de <paramref name="rawResponse"/>: primeiro procura um bloco
    /// cercado por ```json ... ``` (ou ``` ... ```); se não encontrar, usa o trecho entre o
    /// primeiro '[' ou '{' e o último ']' ou '}' correspondente na resposta.
    /// </summary>
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
