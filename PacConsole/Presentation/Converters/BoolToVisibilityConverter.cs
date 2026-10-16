using Microsoft.UI.Xaml.Data;

namespace PacConsole.Presentation.Converters;

/// <summary>Visible when true; ConverterParameter="Invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var on = value is true;
        if (parameter as string == "Invert") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
