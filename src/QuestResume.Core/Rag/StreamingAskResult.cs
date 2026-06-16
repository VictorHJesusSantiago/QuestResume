using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

/// <summary>
/// Result of <see cref="RagQueryEngine.AskStreamAsync"/>: the retrieved sources (known
/// immediately, before the LLM starts generating) and an async stream of answer text
/// fragments, used by <c>/api/ask/stream</c> for ChatGPT-style incremental responses.
/// </summary>
public sealed class StreamingAskResult
{
    public required IReadOnlyList<SearchResultItem> Sources { get; init; }

    public required IAsyncEnumerable<string> Tokens { get; init; }
}
