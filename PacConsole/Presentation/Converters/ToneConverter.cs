using Microsoft.UI.Xaml.Data;

namespace PacConsole.Presentation.Converters;

/// <summary>
/// Maps a tone key (or a bool, as "True"/"False") to an app resource:
/// ConverterParameter="selected=AccentBrush|alert=AlertBorderBrush|normal=BorderBrushDefault".
/// The last pair is the fallback. Keeps brush choices in XAML and state rules in Core.
/// </summary>
public sealed class ToneConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value?.ToString() ?? "";
        string? resourceKey = null;
        foreach (var pair in (parameter as string ?? "").Split('|'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;
            resourceKey = parts[1];
            if (string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase)) break;
        }
        return resourceKey is not null && Application.Current.Resources.TryGetValue(resourceKey, out var r) ? r : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
