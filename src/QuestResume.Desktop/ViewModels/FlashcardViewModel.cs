using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QuestResume.Desktop.ViewModels;

/// <summary>
/// View-model para um flashcard exibido na aba "Estudo", com estado de virado (mostra
/// pergunta ou resposta) controlado pelo usuário.
/// </summary>
public sealed partial class FlashcardViewModel : ObservableObject
{
    public required string Question { get; init; }

    public required string Answer { get; init; }

    [ObservableProperty]
    private bool isFlipped;

    [RelayCommand]
    private void Flip() => IsFlipped = !IsFlipped;
}
