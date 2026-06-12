namespace QuestResume.Core.Models;

/// <summary>
/// Result of extracting the textual content (and any metadata) from a single file.
/// </summary>
public sealed class ExtractedDocument
{
    public required string Path { get; init; }

    public required string FileName { get; init; }

    public required string Extension { get; init; }

    public string Text { get; init; } = string.Empty;

    public Dictionary<string, string> Metadata { get; init; } = new();

    public DateTime ModifiedUtc { get; init; }
}
