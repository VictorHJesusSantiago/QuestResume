namespace QuestResume.Desktop.ViewModels;

public sealed class ChatEntry
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<SourceReference>? Sources { get; init; }

        public IReadOnlyList<string> RelatedQuestions { get; init; } = Array.Empty<string>();
}

public sealed class SourceReference
{
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }

        public int ChunkIndex { get; init; }

        public int? PageNumber { get; init; }

        public string DisplayLabel => PageNumber is int page ? $"{FileName} (página {page})" : FileName;
}
