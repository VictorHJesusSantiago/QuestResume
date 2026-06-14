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

    /// <summary>
    /// A short fragment of <see cref="ChunkText"/> with matched query terms wrapped in
    /// <c>...</c> markers (control characters chosen because they can't appear in
    /// real text), or <c>null</c> if highlighting wasn't possible. Consumers should split on
    /// these markers to render the matches (e.g. as <c>&lt;mark&gt;</c> elements).
    /// </summary>
    public string? Highlight { get; init; }
}
