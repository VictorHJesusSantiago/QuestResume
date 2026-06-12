namespace QuestResume.Core.Models;

/// <summary>
/// A single chunk returned by the search index in response to a query.
/// </summary>
public sealed class SearchResultItem
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public required int ChunkIndex { get; init; }

    public required string ChunkText { get; init; }

    public required float Score { get; init; }
}
