namespace QuestResume.Core.Models;

/// <summary>
/// The answer produced by the RAG pipeline, together with the chunks used as context.
/// </summary>
public sealed class AskResult
{
    public required string Answer { get; init; }

    public required IReadOnlyList<SearchResultItem> Sources { get; init; }
}
