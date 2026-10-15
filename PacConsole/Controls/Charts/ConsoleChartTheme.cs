using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace PacConsole.Controls.Charts;

/// <summary>
/// Every LiveCharts2 paint the console uses, built from the colour tokens in Styles/Tokens.xaml so
/// a theme switch restyles all three charts (<see cref="Load"/>, then each chart re-applies).
/// </summary>
public static class ConsoleChartTheme
{
    public static SKColor Accent { get; private set; }
    public static SKColor InkSoft { get; private set; }
    public static SKColor Grid { get; private set; }
    public static SKColor Alert { get; private set; }
    public static SKColor AlertText { get; private set; }
    public static SKColor Faint { get; private set; }
    public static SKColor TextPrimary { get; private set; }

    static ConsoleChartTheme() => Load(Application.Current.Resources);

    /// <summary>Reads the chart colours from the current token brushes.</summary>
    public static void Load(ResourceDictionary r)
    {
        static SKColor C(ResourceDictionary r, string key) => r[key] is SolidColorBrush b
            ? new SKColor(b.Color.R, b.Color.G, b.Color.B, b.Color.A)
            : SKColors.Magenta;
        Accent = C(r, "AccentBrush");
        InkSoft = C(r, "InkSoftBrush");
        Grid = C(r, "HairlineBrush");
        Alert = C(r, "AlertBrush");
        AlertText = C(r, "AlertTextBrush");
        Faint = C(r, "MutedBrush");
        TextPrimary = C(r, "TextPrimaryBrush");
    }

    public const float LabelSize = 11;

    public static SolidColorPaint AxisLabels() => new(Faint) { SKTypeface = LabelTypeface };

    public static SolidColorPaint GridLines() => new(Grid) { StrokeThickness = 1 };

    public static SolidColorPaint Stroke(SKColor color, float thickness) => new(color) { StrokeThickness = thickness };

    public static SolidColorPaint Fill(SKColor color, byte alpha) => new(color.WithAlpha(alpha));

    public static SolidColorPaint Dashed(SKColor color, byte alpha = 255, float thickness = 1) =>
        new(color.WithAlpha(alpha)) { StrokeThickness = thickness, PathEffect = new DashEffect([4, 4]) };

    /// <summary>Hidden axis with a fixed range: keeps the chart from rescaling as values move.</summary>
    public static Axis FixedAxis(double min, double max, bool visible, double? step = null, Func<double, string>? labeler = null) => new()
    {
        MinLimit = min,
        MaxLimit = max,
        IsVisible = visible,
        MinStep = step ?? 0,
        ForceStepToMin = step is not null,
        TextSize = LabelSize,
        LabelsPaint = visible ? AxisLabels() : null,
        SeparatorsPaint = visible ? GridLines() : null,
        Labeler = labeler ?? (v => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture)),
    };

    /// <summary>
    /// Recolours a paint in place. Charts never swap paints after they first draw: replacing one
    /// let LiveCharts2 dispose the old paint, and with it the shared label typeface, after which
    /// the chart stopped rendering (output and hourly charts blank or frozen on a theme switch).
    /// </summary>
    public static void Recolor(LiveChartsCore.Painting.Paint? paint, SKColor color, byte alpha = 255)
    {
        if (paint is SolidColorPaint solid) solid.Color = color.WithAlpha(alpha);
    }

    /// <summary>Recolours a visible axis's labels and grid lines in place.</summary>
    public static void Repaint(Axis axis)
    {
        if (!axis.IsVisible) return;
        Recolor(axis.LabelsPaint, Faint);
        Recolor(axis.SeparatorsPaint, Grid);
    }

    /// <summary>IBM Plex Sans from the app's own assets, so chart labels match the XAML type.</summary>
    public static SKTypeface? LabelTypeface { get; set; }
}
