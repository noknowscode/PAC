namespace PacConsole.Styles;

/// <summary>
/// Switches the console between its light (base) and graphite palettes at runtime by repainting the
/// token brushes in place. XAML holds those brushes through StaticResource and ToneConverter hands out
/// the same instances, so every binding follows without re-evaluating (a converter-produced brush would
/// otherwise keep the theme it was first resolved in; see uno-runtime-gotchas, Theming and color).
/// Colour tokens the Skia painters read (Solid*) are replaced, and <see cref="Changed"/> tells the
/// canvases and trend charts to re-read them.
/// </summary>
public static class ConsoleTheme
{
    private static Dictionary<string, Windows.UI.Color>? _light;
    private static ResourceDictionary? _dark;

    public static bool IsDark { get; private set; }

    /// <summary>Raised after a switch, once every token holds its new colour.</summary>
    public static event Action? Changed;

    public static void Apply(bool dark, FrameworkElement? root)
    {
        var app = Application.Current.Resources;
        // Kept out of the app resources: its keys match the token brushes it recolours.
        var palette = _dark ??= new ResourceDictionary { Source = new Uri("ms-appx:///Styles/Palette.Dark.xaml") };
        var keys = palette.Keys.Cast<object>().Select(k => (string)k).ToList();

        // The light values are whatever Tokens.xaml declared; capture them before the first switch.
        _light ??= keys.ToDictionary(k => k, k => ColorOf(app[k]));

        foreach (var key in keys)
        {
            var color = dark ? (Windows.UI.Color)palette[key] : _light[key];
            if (app[key] is SolidColorBrush brush) brush.Color = color;
            else app[key] = color;
        }

        IsDark = dark;
        // Stock control templates (tooltips, text selection, scroll bars) follow the element theme.
        if (root is not null) root.RequestedTheme = dark ? ElementTheme.Dark : ElementTheme.Light;

        // Each listener on its own: one failing chart or canvas must not leave the rest unthemed.
        foreach (var listener in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)listener).Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Theme listener {listener.Target?.GetType().Name} failed: {ex}");
                Log(ex, listener.Target);
            }
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void Log(Exception ex, object? target) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "pacconsole-theme.log"), $"{DateTime.Now:O} {target?.GetType().Name}: {ex}\n");

    private static Windows.UI.Color ColorOf(object value) => value switch
    {
        SolidColorBrush b => b.Color,
        Windows.UI.Color c => c,
        _ => throw new InvalidOperationException($"Theme token is neither a Color nor a SolidColorBrush: {value}"),
    };
}
