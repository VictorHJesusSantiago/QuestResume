using QuestResume.Core.Models;

namespace QuestResume.Core.Rag;

public sealed class StreamingAskResult
{
    public required IReadOnlyList<SearchResultItem> Sources { get; init; }

    public required IAsyncEnumerable<string> Tokens { get; init; }
}
