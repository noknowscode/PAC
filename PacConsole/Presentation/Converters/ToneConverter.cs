using Microsoft.UI.Xaml.Data;
using PacConsole.Styles;

namespace PacConsole.Presentation.Converters;

/// <summary>
/// Maps a tone key (or a bool, as "True"/"False") to a palette brush:
/// ConverterParameter="selected=AccentBrush|alert=AlertBorderBrush|normal=BorderBrushDefault".
/// The last pair is the fallback. Keeps brush choices in XAML and state rules in Core. The brushes come
/// from <see cref="ThemeBrushes"/>, so they follow a theme switch without the binding re-running.
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
        return resourceKey is null ? null : ThemeBrushes.Get(resourceKey);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
