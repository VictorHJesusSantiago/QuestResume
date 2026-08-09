using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuestResume.Desktop.ViewModels;

public sealed partial class FlashcardViewModel : ObservableObject
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

    [ObservableProperty]
    private bool isFlipped;

    [RelayCommand]
    private void Flip() => IsFlipped = !IsFlipped;
}
