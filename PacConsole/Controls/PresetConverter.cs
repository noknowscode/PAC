using Microsoft.UI.Xaml.Data;

namespace PacConsole.Controls;

/// <summary>
/// A standard-view toggle's IsChecked, bound two-way to <see cref="PlantViewport.RestingPreset"/>: checked while the camera
/// rests in the view named by the parameter; checking it asks for that view. Unchecking asks for nothing (null).
/// </summary>
public sealed class PresetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string key && key == parameter as string;

    // Null is the "no request" the viewport ignores; the interface is declared without nullability.
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is true && parameter is string key ? key : null!;
}
