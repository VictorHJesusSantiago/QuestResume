namespace QuestResume.Desktop.ViewModels;

public sealed class ChatEntry
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public IReadOnlyList<SourceReference>? Sources { get; init; }
}

/// <summary>
/// A clickable reference to an indexed source file, shown below a chat answer so the user can
/// open the original document via <see cref="MainViewModel.OpenSourceCommand"/>.
/// </summary>
public sealed class SourceReference
{
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }
}
