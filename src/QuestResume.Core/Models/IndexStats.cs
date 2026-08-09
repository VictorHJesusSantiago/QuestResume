namespace QuestResume.Core.Models;

public sealed class IndexStats
{
    public int FilesProcessed { get; set; }

    public int FilesSkipped { get; set; }

    public int ChunksIndexed { get; set; }

        public int FilesRemoved { get; set; }

    public List<string> SkippedFiles { get; } = new();

    public List<string> Errors { get; } = new();

    public List<DuplicateFile> Duplicates { get; } = new();

        public List<NearDuplicateFile> NearDuplicates { get; } = new();
}
