namespace QuestResume.Desktop.ViewModels;

/// <summary>
/// Parâmetro composto (pergunta + índice da alternativa clicada) usado pelo
/// <c>SelectQuizOptionCommand</c>, já que um <see cref="System.Windows.Controls.ItemsControl"/>
/// só consegue passar um único <c>CommandParameter</c> por botão.
/// </summary>
public sealed record QuizOptionSelection(QuizQuestionViewModel Question, int OptionIndex, string Text);
