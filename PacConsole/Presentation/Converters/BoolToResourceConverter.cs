using Microsoft.UI.Xaml.Data;

namespace PacConsole.Presentation.Converters;

/// <summary>
/// Picks one of two app resources by a bool: ConverterParameter="TrueKey|FalseKey".
/// Lets templates switch brushes for selected / alert / held states without a view model
/// knowing about brushes.
/// </summary>
public sealed class BoolToResourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var keys = (parameter as string ?? "").Split('|');
        var key = value is true ? keys[0] : keys.ElementAtOrDefault(1) ?? keys[0];
        return Application.Current.Resources.TryGetValue(key, out var resource) ? resource : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

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
