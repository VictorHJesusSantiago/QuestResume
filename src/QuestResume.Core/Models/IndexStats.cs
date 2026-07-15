namespace QuestResume.Core.Models;

/// <summary>
/// Summary of what happened during an indexing run.
/// </summary>
public sealed class IndexStats
{
    public int FilesProcessed { get; set; }

    public int FilesSkipped { get; set; }

    public int ChunksIndexed { get; set; }

    /// <summary>
    /// Number of files removed from the index during this run because they no longer exist on
    /// disk (only tracked in delta/incremental mode — see
    /// <see cref="QuestResume.Core.Configuration.AppOptions.IncrementalIndexingEnabled"/>).
    /// </summary>
    public int FilesRemoved { get; set; }

    public List<string> SkippedFiles { get; } = new();

    public List<string> Errors { get; } = new();

    public List<DuplicateFile> Duplicates { get; } = new();

    /// <summary>
    /// Files flagged by semantic deduplication (<see cref="QuestResume.Core.Configuration.AppOptions.SemanticDeduplicationEnabled"/>)
    /// as likely near-duplicates of an already indexed document. Informational only — nothing is removed.
    /// </summary>
    public List<NearDuplicateFile> NearDuplicates { get; } = new();
}
