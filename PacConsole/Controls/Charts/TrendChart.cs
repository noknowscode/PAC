using Microsoft.UI.Xaml.Automation;
using PacConsole.Core.Console;
using PacConsole.Styles;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Controls.Charts;

/// <summary>
/// One row of the synchronized trends: a reading's line over the range, three value labels on the
/// left, start / middle / end times below, a fill under the line that fades from 24% accent at the
/// top of the plot to nothing at the baseline, its target or limit as a dashed line, the latest alert
/// onset as a dashed mark and the shared cursor as a dotted hairline with a dot on the line.
/// The labels are XAML text (the type ramp and theme carry them); the marks are drawn on a small
/// Skia canvas. Every row gets the same label column width, so the plots share their edges.
/// </summary>
/// <remarks>
/// A bespoke Skia plot rather than LiveCharts2: small multiples with a shared cursor and event marks
/// are four lines and a few hairlines each, and LiveCharts' per-chart typefaces and animation loop
/// were the measured costs of the previous page (HANDOFF, performance).
/// </remarks>
public sealed partial class TrendChart : UserControl
{
    private readonly TrendCanvas _canvas = new();
    private readonly TextBlock _max = Label(VerticalAlignment.Top);
    private readonly TextBlock _mid = Label(VerticalAlignment.Center);
    private readonly TextBlock _min = Label(VerticalAlignment.Bottom);
    private readonly TextBlock _start = Label(VerticalAlignment.Top, HorizontalAlignment.Left);
    private readonly TextBlock _middle = Label(VerticalAlignment.Top, HorizontalAlignment.Center);
    private readonly TextBlock _end = Label(VerticalAlignment.Top, HorizontalAlignment.Right);
    private readonly TextBlock _reference = Label(VerticalAlignment.Top, HorizontalAlignment.Right);

    public TrendChart()
    {
        // Layout values come from Layout.xaml, like the XAML pages'; only the Skia marks below are drawn in code.
        var root = new Grid { ColumnSpacing = Token<double>("Space8"), RowSpacing = Token<double>("Space2") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Token<double>("TrendAxisLabelWidth")) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labels = new Grid { Margin = Token<Thickness>("TrendAxisLabelOverhang") };
        labels.Children.Add(_max);
        labels.Children.Add(_mid);
        labels.Children.Add(_min);
        foreach (var t in new[] { _max, _mid, _min }) t.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(labels);

        var plot = new Grid();
        Grid.SetColumn(plot, 1);
        plot.Children.Add(_canvas);
        _reference.Margin = Token<Thickness>("TrendReferenceLift");
        plot.Children.Add(_reference);
        root.Children.Add(plot);

        var times = new Grid();
        Grid.SetColumn(times, 1);
        Grid.SetRow(times, 1);
        times.Children.Add(_start);
        times.Children.Add(_middle);
        times.Children.Add(_end);
        root.Children.Add(times);

        Content = root;
        AutomationProperties.SetAccessibilityView(_canvas, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => OnTheme();
        ActualThemeChanged += (_, _) => OnTheme();
    }

    private static T Token<T>(string key) => (T)Application.Current.Resources[key];

    private static TextBlock Label(VerticalAlignment v, HorizontalAlignment h = HorizontalAlignment.Stretch) => new()
    {
        Style = (Style)Application.Current.Resources["TypeMicro"],
        VerticalAlignment = v,
        HorizontalAlignment = h,
    };

    /// <summary>The row's reading (a <see cref="TrendSeries"/>).</summary>
    public object? Series
    {
        get => GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(object), typeof(TrendChart), new PropertyMetadata(null, (d, _) => ((TrendChart)d).Update()));

    /// <summary>The shared cursor's sample index.</summary>
    public int Cursor
    {
        get => (int)GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    public static readonly DependencyProperty CursorProperty =
        DependencyProperty.Register(nameof(Cursor), typeof(int), typeof(TrendChart), new PropertyMetadata(-1, (d, _) => ((TrendChart)d).Update()));

    /// <summary>The sample index of the latest alert onset in the range, or -1.</summary>
    public int EventIndex
    {
        get => (int)GetValue(EventIndexProperty);
        set => SetValue(EventIndexProperty, value);
    }

    public static readonly DependencyProperty EventIndexProperty =
        DependencyProperty.Register(nameof(EventIndex), typeof(int), typeof(TrendChart), new PropertyMetadata(-1, (d, _) => ((TrendChart)d).Update()));

    public string TimeStart { get => (string)GetValue(TimeStartProperty); set => SetValue(TimeStartProperty, value); }
    public static readonly DependencyProperty TimeStartProperty = TimeProperty(nameof(TimeStart));
    public string TimeMid { get => (string)GetValue(TimeMidProperty); set => SetValue(TimeMidProperty, value); }
    public static readonly DependencyProperty TimeMidProperty = TimeProperty(nameof(TimeMid));
    public string TimeEnd { get => (string)GetValue(TimeEndProperty); set => SetValue(TimeEndProperty, value); }
    public static readonly DependencyProperty TimeEndProperty = TimeProperty(nameof(TimeEnd));

    private static DependencyProperty TimeProperty(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(TrendChart), new PropertyMetadata("", (d, _) => ((TrendChart)d).UpdateTimes()));

    private void UpdateTimes()
    {
        Set(_start, TimeStart);
        Set(_middle, TimeMid);
        Set(_end, TimeEnd);
    }

    private static void Set(TextBlock t, string text)
    {
        if (t.Text != text) t.Text = text;
    }

    private void Update()
    {
        if (Series is not TrendSeries s)
        {
            _canvas.Show(null, -1, -1);
            return;
        }
        Set(_max, s.Format1(s.Max));
        Set(_mid, s.Format1((s.Min + s.Max) / 2));
        Set(_min, s.Format1(s.Min));
        Set(_reference, s.Target is { } t ? $"Target {s.Format1(t)}" : s.Limit is { } l ? $"Limit {s.Format1(l)}" : "");
        _canvas.Show(s, Cursor, EventIndex);
        var at = Math.Clamp(Cursor < 0 ? s.Values.Length - 1 : Cursor, 0, s.Values.Length - 1);
        AutomationProperties.SetName(this, $"{s.Label} trend, {s.Format1(s.Values[at])} {s.Unit} at the cursor, range {s.Format1(s.Min)} to {s.Format1(s.Max)}");
    }

    private void OnTheme()
    {
        _canvas.ReadColors(ActualTheme);
        Update();
    }

    /// <summary>
    /// The plot itself. RenderOverride runs on the desktop host's render thread, so the UI thread builds each plot and
    /// its inks as immutable values and swaps them in under _drawGate. The Skia objects live from Loaded to Unloaded
    /// (an element can be loaded again) and are freed under the same lock, so no frame draws with a freed paint.
    /// </summary>
    private sealed partial class TrendCanvas : SKCanvasElement
    {
        /// <summary>Alpha of the fill at the top of the plot: 0x3D is 24%.</summary>
        private const byte FillTopAlpha = 0x3D;

        private readonly object _drawGate = new();
        private Paints? _paints;
        private Plot _plot = new([], 0, 1, null, null, -1, -1);
        private Inks _inks = new(default, default, default, default, default);

        private sealed record Plot(double[] Values, double Min, double Max, double? Target, double? Limit, int Cursor, int Event);

        private sealed record Inks(SKColor Accent, SKColor Grid, SKColor Faint, SKColor Ink, SKColor Alert);

        /// <summary>The Skia objects a plot draws with, made once per load.</summary>
        private sealed class Paints : IDisposable
        {
            public SKPaint Line { get; } = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            public SKPaint Hair { get; } = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };
            public SKPaint Dot { get; } = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
            public SKPaint Area { get; } = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
            public SKPathEffect Dashed { get; } = SKPathEffect.CreateDash([4, 3], 0);
            public SKPathEffect Dotted { get; } = SKPathEffect.CreateDash([1.5f, 2.5f], 0);

            // The line and the area under it change with every reading: built per render and detached.
            public SKPathBuilder Builder { get; } = new();

            // The fill's gradient depends only on the plot height and the accent, so it is rebuilt only
            // when one of those changes (a resize or a theme switch), never per frame.
            public SKShader? Gradient { get; set; }
            public (float, SKColor) GradientKey { get; set; }

            public void Dispose()
            {
                Line.Dispose();
                Hair.Dispose();
                Dot.Dispose();
                Area.Dispose();
                Dashed.Dispose();
                Dotted.Dispose();
                Builder.Dispose();
                Gradient?.Dispose();
            }
        }

        public TrendCanvas()
        {
            Loaded += (_, _) =>
            {
                lock (_drawGate) _paints ??= new Paints();
                Invalidate();
            };
            Unloaded += (_, _) =>
            {
                Paints? old;
                lock (_drawGate) (old, _paints) = (_paints, null);
                old?.Dispose();
            };
        }

        public void ReadColors(ElementTheme theme)
        {
            SKColor C(string key)
            {
                var c = ThemeBrushes.Color(key, theme);
                return new SKColor(c.R, c.G, c.B, c.A);
            }
            var inks = new Inks(C("AccentBrush"), C("HairlineBrush"), C("FaintBrush"), C("InkSoftBrush"), C("AlertBrush"));
            lock (_drawGate) _inks = inks;
        }

        public void Show(TrendSeries? s, int cursor, int eventIndex)
        {
            var plot = s is null
                ? new Plot([], 0, 1, null, null, cursor, eventIndex)
                : new Plot([.. s.Values], s.Min, s.Max, s.Target, s.Limit, cursor, eventIndex);
            lock (_drawGate) _plot = plot;
            Invalidate();
        }

        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            lock (_drawGate)
            {
                if (_paints is { } paints) Draw(canvas, area, paints, _plot, _inks);
            }
        }

        private static void Draw(SKCanvas canvas, Size area, Paints p, Plot plot, Inks inks)
        {
            var (w, h) = ((float)area.Width, (float)area.Height);
            if (w <= 0 || h <= 0) return;
            const float inset = 3; // room for the line's stroke and the cursor dot at the edges
            var top = inset;
            var bottom = h - inset;
            var values = plot.Values;
            float Y(double v) => (float)(bottom - (v - plot.Min) / Math.Max(plot.Max - plot.Min, 1e-9) * (bottom - top));
            float X(int i) => values.Length < 2 ? 0 : w * i / (values.Length - 1);

            void HLine(float y, SKColor color, SKPathEffect? effect)
            {
                p.Hair.Color = color;
                p.Hair.PathEffect = effect;
                canvas.DrawLine(0, y, w, y, p.Hair);
            }

            void VLine(float x, SKColor color, SKPathEffect effect)
            {
                p.Hair.Color = color;
                p.Hair.PathEffect = effect;
                canvas.DrawLine(x, 0, x, h, p.Hair);
            }

            // Three gridlines, at the labelled values.
            HLine(top, inks.Grid, null);
            HLine((top + bottom) / 2, inks.Grid, null);
            HLine(bottom, inks.Grid, null);
            if (values.Length < 2) return;

            if (plot.Target is { } t) HLine(Y(t), inks.Faint, p.Dashed);
            if (plot.Limit is { } l) HLine(Y(l), inks.Alert.WithAlpha(0xB0), p.Dashed);
            if (plot.Event >= 0 && plot.Event < values.Length) VLine(X(plot.Event), inks.Alert.WithAlpha(0xB0), p.Dashed);

            var pb = p.Builder;
            pb.MoveTo(X(0), Y(values[0]));
            for (var i = 1; i < values.Length; i++) pb.LineTo(X(i), Y(values[i]));
            using var trend = pb.Snapshot();

            // The area under the line, closed along the baseline, under a vertical fade.
            if (p.Gradient is null || p.GradientKey != (h, inks.Accent))
            {
                p.Gradient?.Dispose();
                p.Gradient = SKShader.CreateLinearGradient(
                    new SKPoint(0, top), new SKPoint(0, bottom),
                    [inks.Accent.WithAlpha(FillTopAlpha), inks.Accent.WithAlpha(0)],
                    SKShaderTileMode.Clamp);
                p.GradientKey = (h, inks.Accent);
                p.Area.Shader = p.Gradient;
            }
            pb.LineTo(X(values.Length - 1), bottom);
            pb.LineTo(X(0), bottom);
            pb.Close();
            using (var under = pb.Detach()) canvas.DrawPath(under, p.Area);

            p.Line.Color = inks.Accent;
            canvas.DrawPath(trend, p.Line);

            var c = plot.Cursor < 0 || plot.Cursor >= values.Length ? values.Length - 1 : plot.Cursor;
            VLine(X(c), inks.Ink, p.Dotted);
            p.Dot.Color = inks.Accent;
            canvas.DrawCircle(X(c), Y(values[c]), 3, p.Dot);
        }
    }
}
