using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuestResume.Desktop.Converters;

/// <summary>
/// Converts <c>null</c> (or an empty collection) to <see cref="Visibility.Collapsed"/> and any
/// other value to <see cref="Visibility.Visible"/>. Used to hide the "Fontes" row of a
/// <see cref="QuestResume.Desktop.ViewModels.ChatEntry"/> when it has no sources.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return Visibility.Collapsed;
        }

        if (value is System.Collections.ICollection collection)
        {
            return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
