using CommunityToolkit.Mvvm.ComponentModel;

namespace QuestResume.Desktop.ViewModels;

/// <summary>
/// View-model wrapper around <see cref="QuestResume.Core.Models.IndexedFileInfo"/> for the
/// "Documentos" tab, adding an editable <see cref="TagsInput"/> (comma-separated) so the user
/// can view and update a document's tags.
/// </summary>
public sealed partial class IndexedDocumentViewModel : ObservableObject
{
    public required string SourcePath { get; init; }

    public required string FileName { get; init; }

    public int ChunkCount { get; init; }

    [ObservableProperty]
    private string tagsInput = string.Empty;
}
