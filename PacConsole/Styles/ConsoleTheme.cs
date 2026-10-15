namespace PacConsole.Styles;

/// <summary>
/// Switches the console between its dark and light palettes at runtime by repainting the token
/// brushes in place. XAML holds those brushes through StaticResource and ToneConverter hands out the
/// same instances, so every binding follows without re-evaluating (a converter-produced brush would
/// otherwise keep the theme it was first resolved in; see uno-runtime-gotchas, Theming and color).
/// Colour tokens the Skia painters read (Solid*) are replaced, and <see cref="Changed"/> tells the
/// canvases and charts to re-read them.
/// </summary>
public static class ConsoleTheme
{
    private static Dictionary<string, Windows.UI.Color>? _dark;
    private static ResourceDictionary? _light;

    public static bool IsLight { get; private set; }

    /// <summary>Raised after a switch, once every token holds its new colour.</summary>
    public static event Action? Changed;

    public static void Apply(bool light, FrameworkElement? root)
    {
        var app = Application.Current.Resources;
        // Kept out of the app resources: its keys match the token brushes it recolours.
        var palette = _light ??= new ResourceDictionary { Source = new Uri("ms-appx:///Styles/Palette.Light.xaml") };
        var keys = palette.Keys.Cast<object>().Select(k => (string)k).ToList();

        // The dark values are whatever Tokens.xaml declared; capture them before the first switch.
        _dark ??= keys.ToDictionary(k => k, k => ColorOf(app[k]));

        foreach (var key in keys)
        {
            var color = light ? (Windows.UI.Color)palette[key] : _dark[key];
            if (app[key] is SolidColorBrush brush) brush.Color = color;
            else app[key] = color;
        }

        IsLight = light;
        // Stock control templates (tooltips, text selection, scroll bars) follow the element theme.
        if (root is not null) root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        Controls.Charts.ConsoleChartTheme.Load(app);

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
