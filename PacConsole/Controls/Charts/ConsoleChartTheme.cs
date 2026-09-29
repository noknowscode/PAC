using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace PacConsole.Controls.Charts;

/// <summary>
/// Every LiveCharts2 paint the console uses, built once from the colour tokens so a token
/// change restyles all three charts. Values match Styles/Tokens.xaml.
/// </summary>
public static class ConsoleChartTheme
{
    public static readonly SKColor Accent = SKColor.Parse("#F5A83B");
    public static readonly SKColor Cyan = SKColor.Parse("#8ADCFF");
    public static readonly SKColor CyanLine = new(110, 210, 255);
    public static readonly SKColor Alert = SKColor.Parse("#FF6F61");
    public static readonly SKColor AlertText = SKColor.Parse("#FF8A7E");
    public static readonly SKColor Faint = SKColor.Parse("#6F95A8");
    public static readonly SKColor TextPrimary = SKColor.Parse("#DFF5FF");

    public const float LabelSize = 10;

    public static SolidColorPaint AxisLabels() => new(Faint) { SKTypeface = MonoTypeface };

    public static SolidColorPaint GridLines() => new(CyanLine.WithAlpha(31)) { StrokeThickness = 1 };

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

    /// <summary>IBM Plex Mono from the app's own assets, so chart labels match the XAML type.</summary>
    public static SKTypeface? MonoTypeface { get; set; }
}
