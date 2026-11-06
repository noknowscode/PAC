namespace PacConsole.Styles;

/// <summary>
/// The palette for code: brushes picked by converters and controls, and the colours Skia painters read. A key is a
/// role of the Simple theme (<c>OnSurfaceBrush</c>, <c>PrimaryColor</c>: the palette is
/// Styles/ColorPaletteOverride.xaml) or one of the console's no-role colours in App.xaml (<c>AlertBrush</c>,
/// <c>SolidShade0Color</c>).
/// A converter or an <c>Application.Current.Resources</c> lookup sees only the app theme and is never asked again
/// after a switch (uno-runtime-gotchas, Theming and color). So <see cref="Get"/> hands out one live brush per key, a
/// copy owned here, and <see cref="Track"/> recolours them all whenever the window root's theme changes. When the
/// palette itself changes (Studio's Themes tab editing the palette under Hot Reload regenerates the theme's role
/// brushes in place) they are recoloured too, and <see cref="PaletteChanged"/> tells the Skia painters to re-read.
/// XAML keeps using {ThemeResource}.
/// </summary>
public static class ThemeBrushes
{
    private static readonly Dictionary<(string Key, double Opacity), SolidColorBrush> Brushes = [];
    private static readonly HashSet<SolidColorBrush> Watched = new(ReferenceEqualityComparer.Instance);
    private static ElementTheme _theme = ElementTheme.Light;
    private static bool _recolourQueued;

    /// <summary>
    /// Raised after the palette changed under the running app (not on a Light/Dark switch, which painters follow
    /// through their own ActualThemeChanged): code that keeps colours rather than brushes re-reads them.
    /// </summary>
    public static event EventHandler? PaletteChanged;

    /// <summary>
    /// The live brush for a key, in the tracked root's theme; with <paramref name="opacity"/>, the key's colour at that
    /// alpha (a colour derived from the theme's, such as the tag plates' translucent ground).
    /// </summary>
    public static SolidColorBrush Get(string key, double opacity = 1)
    {
        if (!Brushes.TryGetValue((key, opacity), out var brush))
        {
            brush = new SolidColorBrush(WithAlpha(Color(key, _theme), opacity));
            Brushes[(key, opacity)] = brush;
        }
        return brush;
    }

    /// <summary>
    /// A colour in an explicit theme, for painters that follow their own element's theme. A brush key carries its
    /// transparency in Opacity (the state brushes are a role at 8 to 64 %), folded into the alpha here.
    /// </summary>
    public static Windows.UI.Color Color(string key, ElementTheme theme) =>
        Find(key, theme) switch
        {
            SolidColorBrush b => WithAlpha(Watch(b).Color, b.Opacity),
            Windows.UI.Color c => WatchBrushOf(key, theme, c),
            var other => throw new InvalidOperationException($"Theme key {key} is neither a Color nor a SolidColorBrush: {other}"),
        };

    /// <summary>Follows the window root: the brushes take its theme now and on every change.</summary>
    public static void Track(FrameworkElement root)
    {
        root.ActualThemeChanged += (sender, _) => Repaint(sender.ActualTheme);
        Repaint(root.ActualTheme);
    }

    private static void Repaint(ElementTheme theme)
    {
        _theme = theme;
        foreach (var ((key, opacity), brush) in Brushes) brush.Color = WithAlpha(Color(key, theme), opacity);
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color c, double opacity) =>
        opacity >= 1 ? c : Windows.UI.Color.FromArgb((byte)Math.Round(c.A * opacity), c.R, c.G, c.B);

    // A palette colour (PrimaryColor) has a role brush beside it (PrimaryBrush); watching that brush is how a palette
    // edit reaches code that only ever asked for the colour.
    private static Windows.UI.Color WatchBrushOf(string key, ElementTheme theme, Windows.UI.Color c)
    {
        if (key.EndsWith("Color", StringComparison.Ordinal) && Find(key[..^5] + "Brush", theme) is SolidColorBrush b) Watch(b);
        return c;
    }

    /// <summary>
    /// The theme recolours its role brushes in place when the palette changes. Watching the ones code has read tells
    /// this registry to re-read too; the recolour runs once, after the theme has finished.
    /// </summary>
    private static SolidColorBrush Watch(SolidColorBrush roleBrush)
    {
        if (Watched.Add(roleBrush))
        {
            roleBrush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => QueueRecolour());
        }
        return roleBrush;
    }

    private static void QueueRecolour()
    {
        if (_recolourQueued || Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is not { } queue) return;
        _recolourQueued = true;
        queue.TryEnqueue(() =>
        {
            _recolourQueued = false;
            Repaint(_theme);
            PaletteChanged?.Invoke(null, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Looks a key up for an explicit theme. Code has no {ThemeResource}, and a plain Application resource lookup
    /// answers with the application's theme, not the window's, so this walks the merged dictionaries' theme
    /// dictionaries itself, the theme's generated palette and role brushes included (Graphite is keyed "Default",
    /// the WinUI convention, or "Dark"), then falls back to the invariant resources.
    /// </summary>
    private static object? Find(string key, ElementTheme theme)
    {
        if (Application.Current?.Resources is not { } resources) return null;
        string[] themeKeys = theme == ElementTheme.Dark ? ["Dark", "Default"] : ["Light"];
        return FindThemed(resources, key, themeKeys) ?? (resources.TryGetValue(key, out var direct) ? direct : null);
    }

    /// <summary>
    /// Depth-first through merged dictionaries, last first: a later dictionary wins in WinUI. Each dictionary answers
    /// with its own theme dictionary for the theme before the dictionaries it merges.
    /// </summary>
    private static object? FindThemed(ResourceDictionary dictionary, string key, string[] themeKeys)
    {
        foreach (var themeKey in themeKeys)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(themeKey, out var t) && t is ResourceDictionary themed
                && themed.TryGetValue(key, out var value))
            {
                return value;
            }
        }
        for (var i = dictionary.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (FindThemed(dictionary.MergedDictionaries[i], key, themeKeys) is { } found) return found;
        }
        return null;
    }
}
