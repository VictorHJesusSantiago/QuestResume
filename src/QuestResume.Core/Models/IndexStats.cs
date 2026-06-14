namespace QuestResume.Core.Models;

/// <summary>
/// Summary of what happened during an indexing run.
/// </summary>
public sealed class IndexStats
{
    public int FilesProcessed { get; set; }

    public int FilesSkipped { get; set; }

    public int ChunksIndexed { get; set; }

    public List<string> SkippedFiles { get; } = new();

    public List<string> Errors { get; } = new();

    public List<DuplicateFile> Duplicates { get; } = new();
}
