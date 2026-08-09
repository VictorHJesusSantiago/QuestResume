using System.Windows;
using System.Windows.Controls;
using QuestResume.Desktop.Services;

namespace QuestResume.Desktop.Behaviors;

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
