using System.Windows;
using System.Windows.Controls;
using QuestResume.Desktop.Services;

namespace QuestResume.Desktop.Behaviors;

/// <summary>
/// Attached property that renders basic Markdown (bold/italic/inline code, via
/// <see cref="SimpleMarkdownParser"/>) into a <see cref="TextBlock"/>'s <see cref="TextBlock.Inlines"/>,
/// used in place of a direct <c>Text="{Binding ...}"</c> binding so LLM answers in the chat show
/// simple formatting instead of literal <c>**asterisks**</c>. Kept as an attached property
/// (rather than restructuring the existing string-based bindings) so it drops into the XAML with
/// a single extra attribute: <c>behaviors:MarkdownTextBehavior.MarkdownText="{Binding Text}"</c>.
/// </summary>
public static class MarkdownTextBehavior
{
    public static readonly DependencyProperty MarkdownTextProperty = DependencyProperty.RegisterAttached(
        "MarkdownText",
        typeof(string),
        typeof(MarkdownTextBehavior),
        new PropertyMetadata(null, OnMarkdownTextChanged));

    public static string? GetMarkdownText(DependencyObject obj) => (string?)obj.GetValue(MarkdownTextProperty);

    public static void SetMarkdownText(DependencyObject obj, string? value) => obj.SetValue(MarkdownTextProperty, value);

    private static void OnMarkdownTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        foreach (var inline in SimpleMarkdownParser.Parse(e.NewValue as string))
        {
            textBlock.Inlines.Add(inline);
        }
    }
}
