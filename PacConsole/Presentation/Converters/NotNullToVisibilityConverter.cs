using Microsoft.UI.Xaml.Data;

namespace PacConsole.Presentation.Converters;

/// <summary>Visible when the value is not null: fixed slots (reading tiles, trend rows, chips) hide when empty.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
