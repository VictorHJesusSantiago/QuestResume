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

    /// <summary>
    /// Number (1-based) of the page where this chunk begins, when the source document has page
    /// boundaries (currently only PDFs, via <see cref="QuestResume.Core.Extraction.Extractors.PdfExtractor"/>
    /// page markers). <c>null</c> for file types without page information.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Parent-child chunking (<see cref="QuestResume.Core.Configuration.AppOptions.ParentChildChunkingEnabled"/>):
    /// when set, this chunk is a small "child" used for search/embedding, and this property holds
    /// the larger "parent" chunk's text that should be returned/used in the LLM prompt instead of
    /// <see cref="Text"/>. <c>null</c> for every other chunking mode.
    /// </summary>
    public string? ParentText { get; init; }
}
