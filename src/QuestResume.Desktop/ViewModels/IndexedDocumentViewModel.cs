using CommunityToolkit.Mvvm.ComponentModel;

namespace QuestResume.Desktop.ViewModels;

public sealed partial class IndexedDocumentViewModel : ObservableObject
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public int ChunkCount { get; init; }

    [ObservableProperty]
    private string tagsInput = string.Empty;

        public string Summary { get; init; } = string.Empty;
}
