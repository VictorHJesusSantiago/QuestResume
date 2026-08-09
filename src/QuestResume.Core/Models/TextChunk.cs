namespace QuestResume.Core.Models;

public sealed class TextChunk
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public required int ChunkIndex { get; init; }

    public required string Text { get; init; }

    public DateTime ModifiedUtc { get; init; }

        public int? PageNumber { get; init; }

        public string? ParentText { get; init; }
}
