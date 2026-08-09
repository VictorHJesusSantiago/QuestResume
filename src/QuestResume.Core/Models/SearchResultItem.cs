namespace QuestResume.Core.Models;

public sealed class SearchResultItem
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public required int ChunkIndex { get; init; }

    public required string ChunkText { get; init; }

    public required float Score { get; init; }

        public string? Highlight { get; init; }

        public int? PageNumber { get; init; }

        public DateTime ModifiedUtc { get; init; }

        public long SizeBytes { get; init; }
}
