// The two XAML types SolidPalette.FromResources reads, so the product's own token mapping runs
// unchanged outside the app. The tool fills the dictionary from Styles/Tokens.xaml (and
// Styles/Palette.Dark.xaml for graphite) exactly as the app's merged resources would hold it.

namespace PacConsole.Rendering
{
    public sealed class ResourceDictionary : Dictionary<string, object>;

    public sealed class SolidColorBrush
    {
        public Windows.UI.Color Color { get; init; }
    }
}

namespace Windows.UI
{
    public readonly record struct Color(byte A, byte R, byte G, byte B);
}
