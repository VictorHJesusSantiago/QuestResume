using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace QuestResume.Desktop.ViewModels;

/// <summary>
/// View-model para uma pergunta de quiz exibida na aba "Estudo". Após o usuário escolher uma
/// alternativa (<see cref="SelectedOptionIndex"/>), <see cref="IsAnswered"/> passa a <c>true</c>
/// e a UI mostra se a resposta escolhida está certa ou errada.
/// </summary>
public sealed partial class QuizQuestionViewModel : ObservableObject
{
    public required string Question { get; init; }

    public required IReadOnlyList<string> Options { get; init; }

    public required int CorrectOptionIndex { get; init; }

    [ObservableProperty]
    private int selectedOptionIndex = -1;

    [ObservableProperty]
    private bool isAnswered;

    public bool IsCorrect => SelectedOptionIndex == CorrectOptionIndex;

    /// <summary>
    /// Wraps <see cref="Options"/> with their index so the XAML <c>ItemsControl</c> can pass a
    /// ready-made <see cref="QuizOptionSelection"/> as a button's <c>CommandParameter</c>
    /// without needing a value converter.
    /// </summary>
    public IReadOnlyList<QuizOptionSelection> IndexedOptions =>
        Options.Select((text, index) => new QuizOptionSelection(this, index, text)).ToList();

    public void SelectOption(int index)
    {
        if (IsAnswered) return;
        SelectedOptionIndex = index;
        IsAnswered = true;
        OnPropertyChanged(nameof(IsCorrect));
    }
}
