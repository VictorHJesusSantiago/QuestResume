namespace QuestResume.Desktop.ViewModels;

public sealed class ChatEntry
{
    public required string Role { get; init; }
    public required string Text { get; init; }
    public string? Sources { get; init; }
}
