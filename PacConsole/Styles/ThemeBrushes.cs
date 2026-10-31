namespace PacConsole.Styles;

/// <summary>
/// Palette colours for code: brushes picked by converters and controls, and the colours Skia painters read.
/// A converter or an <c>Application.Current.Resources</c> lookup sees only the app theme and is never asked
/// again after a switch (uno-runtime-gotchas, Theming and color). So <see cref="Get"/> hands out one live
/// brush per key, a copy owned here, and <see cref="Track"/> repaints them all from Palette.xaml whenever
/// the window root's theme changes. XAML keeps using {ThemeResource}.
/// </summary>
public static class ThemeBrushes
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = [];
    private static readonly Lazy<ResourceDictionary> Palette =
        new(() => new ResourceDictionary { Source = new Uri("ms-appx:///Styles/Palette.xaml") });

    private static ElementTheme _theme = ElementTheme.Light;

    /// <summary>The live brush for a palette key, in the tracked root's theme.</summary>
    public static SolidColorBrush Get(string key)
    {
        if (!Brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(Color(key, _theme));
            Brushes[key] = brush;
        }
        return brush;
    }

    /// <summary>A palette colour in an explicit theme, for painters that follow their own element's theme.</summary>
    public static Windows.UI.Color Color(string key, ElementTheme theme)
    {
        var name = theme == ElementTheme.Dark ? "Dark" : "Light";
        var dictionary = (ResourceDictionary)Palette.Value.ThemeDictionaries[name];
        return dictionary[key] switch
        {
            SolidColorBrush b => b.Color,
            Windows.UI.Color c => c,
            var other => throw new InvalidOperationException($"Palette key {key} is neither a Color nor a SolidColorBrush: {other}"),
        };
    }

    /// <summary>Follows the window root: the brushes take its theme now and on every change.</summary>
    public static void Track(FrameworkElement root)
    {
        root.ActualThemeChanged += (sender, _) => Repaint(sender.ActualTheme);
        Repaint(root.ActualTheme);
    }

    private static void Repaint(ElementTheme theme)
    {
        _theme = theme;
        foreach (var (key, brush) in Brushes) brush.Color = Color(key, theme);
    }
}
