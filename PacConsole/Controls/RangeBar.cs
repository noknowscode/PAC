using Microsoft.UI.Xaml.Shapes;
using PacConsole.Styles;

namespace PacConsole.Controls;

/// <summary>
/// A reading against its range, as ISA-101 displays show it: a 4 px track, the normal band, the alarm
/// limit as a tick and the value as a marker that turns to the alert ink outside the band. Positions
/// are fractions of the span, computed in Core (<c>ReadingTileView</c>). No platform or Toolkit
/// control draws a band with a limit and a marker (ProgressBar shows a value only).
/// </summary>
// xaml-lint: allow builtin - ProgressBar shows one value; this needs a band, a limit tick and a marker on one track (WCT RangeSelector is a two-thumb input).
public sealed partial class RangeBar : Canvas
{
    private readonly Rectangle _track = new() { Height = 4, RadiusX = 2, RadiusY = 2 };
    private readonly Rectangle _band = new() { Height = 4 };
    private readonly Rectangle _limit = new() { Width = 2, Height = 10 };
    private readonly Rectangle _marker = new() { Width = 2, Height = 12, RadiusX = 1, RadiusY = 1 };

    public RangeBar()
    {
        Height = 12;
        IsHitTestVisible = false;
        Children.Add(_track);
        Children.Add(_band);
        Children.Add(_limit);
        Children.Add(_marker);
        _track.Fill = ThemeBrushes.Get("FillBrush");
        _band.Fill = ThemeBrushes.Get("FillStrongBrush");
        _limit.Fill = ThemeBrushes.Get("AlertBrush");
        SizeChanged += (_, _) => Arrange();
    }

    public double BandStart { get => (double)GetValue(BandStartProperty); set => SetValue(BandStartProperty, value); }
    public static readonly DependencyProperty BandStartProperty = Fraction(nameof(BandStart), 0);

    public double BandEnd { get => (double)GetValue(BandEndProperty); set => SetValue(BandEndProperty, value); }
    public static readonly DependencyProperty BandEndProperty = Fraction(nameof(BandEnd), 1);

    /// <summary>The alarm limit's position, or a negative value for none.</summary>
    public double LimitAt { get => (double)GetValue(LimitAtProperty); set => SetValue(LimitAtProperty, value); }
    public static readonly DependencyProperty LimitAtProperty = Fraction(nameof(LimitAt), -1);

    public double ValueAt { get => (double)GetValue(ValueAtProperty); set => SetValue(ValueAtProperty, value); }
    public static readonly DependencyProperty ValueAtProperty = Fraction(nameof(ValueAt), 0);

    public bool OutOfBand { get => (bool)GetValue(OutOfBandProperty); set => SetValue(OutOfBandProperty, value); }
    public static readonly DependencyProperty OutOfBandProperty =
        DependencyProperty.Register(nameof(OutOfBand), typeof(bool), typeof(RangeBar), new PropertyMetadata(false, (d, _) => ((RangeBar)d).Arrange()));

    private static DependencyProperty Fraction(string name, double fallback) =>
        DependencyProperty.Register(name, typeof(double), typeof(RangeBar), new PropertyMetadata(fallback, (d, _) => ((RangeBar)d).Arrange()));

    private void Arrange()
    {
        var w = ActualWidth;
        if (w <= 0) return;
        Place(_track, 0, 4, w);
        Place(_band, BandStart * w, 4, Math.Max(0, (BandEnd - BandStart) * w));
        _limit.Visibility = LimitAt >= 0 ? Visibility.Visible : Visibility.Collapsed;
        Place(_limit, Math.Clamp(LimitAt * w - 1, 0, w - 2), 1, null);
        Place(_marker, Math.Clamp(ValueAt * w - 1, 0, w - 2), 0, null);
        // ThemeBrushes repaints these on a theme switch, so choosing one here is enough.
        _marker.Fill = ThemeBrushes.Get(OutOfBand ? "AlertTextBrush" : "TextPrimaryBrush");
    }

    private static void Place(Rectangle r, double x, double y, double? width)
    {
        SetLeft(r, x);
        SetTop(r, y);
        if (width is { } w) r.Width = w;
    }
}
