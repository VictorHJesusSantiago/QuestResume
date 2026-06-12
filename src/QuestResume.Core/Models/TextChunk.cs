namespace QuestResume.Core.Models;

/// <summary>
/// A slice of a document's text, sized to fit comfortably in the LLM context window.
/// </summary>
public sealed class TextChunk
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public required int ChunkIndex { get; init; }

    public required string Text { get; init; }

    public DateTime ModifiedUtc { get; init; }
}
