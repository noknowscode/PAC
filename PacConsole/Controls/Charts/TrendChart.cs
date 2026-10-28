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
        var root = new Grid { ColumnSpacing = 8, RowSpacing = 2 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labels = new Grid { Margin = new Thickness(0, -6, 0, -6) };
        labels.Children.Add(_max);
        labels.Children.Add(_mid);
        labels.Children.Add(_min);
        foreach (var t in new[] { _max, _mid, _min }) t.HorizontalAlignment = HorizontalAlignment.Right;
        root.Children.Add(labels);

        var plot = new Grid();
        Grid.SetColumn(plot, 1);
        plot.Children.Add(_canvas);
        _reference.Margin = new Thickness(0, -16, 0, 0);
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

    /// <summary>The plot itself. State is snapshotted on the UI thread; RenderOverride only reads it.</summary>
    private sealed partial class TrendCanvas : SKCanvasElement
    {
        private readonly SKPaint _line = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _hair = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };
        private readonly SKPaint _dot = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPaint _area = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private readonly SKPath _fill = new();

        // The fill's gradient depends only on the plot height and the accent, so it is rebuilt only
        // when one of those changes (a resize or a theme switch), never per frame.
        private SKShader? _gradient;
        private (float, SKColor) _gradientKey;

        /// <summary>Alpha of the fill at the top of the plot: 0x3D is 24%.</summary>
        private const byte FillTopAlpha = 0x3D;
        private readonly SKPathEffect _dashed = SKPathEffect.CreateDash([4, 3], 0);
        private readonly SKPathEffect _dotted = SKPathEffect.CreateDash([1.5f, 2.5f], 0);
        private readonly SKPath _path = new();

        private SKColor _accent, _grid, _faint, _ink, _alert;
        private double[] _values = [];
        private double _min, _max;
        private double? _target, _limit;
        private int _cursor = -1, _event = -1;

        public void ReadColors(ElementTheme theme)
        {
            SKColor C(string key)
            {
                var c = ThemeBrushes.Color(key, theme);
                return new SKColor(c.R, c.G, c.B, c.A);
            }
            _accent = C("AccentBrush");
            _grid = C("HairlineBrush");
            _faint = C("FaintBrush");
            _ink = C("InkSoftBrush");
            _alert = C("AlertBrush");
        }

        public void Show(TrendSeries? s, int cursor, int eventIndex)
        {
            _values = s is null ? [] : [.. s.Values];
            (_min, _max, _target, _limit) = s is null ? (0, 1, null, null) : (s.Min, s.Max, s.Target, s.Limit);
            _cursor = cursor;
            _event = eventIndex;
            Invalidate();
        }

        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var (w, h) = ((float)area.Width, (float)area.Height);
            if (w <= 0 || h <= 0) return;
            const float inset = 3; // room for the line's stroke and the cursor dot at the edges
            var top = inset;
            var bottom = h - inset;
            float Y(double v) => (float)(bottom - (v - _min) / Math.Max(_max - _min, 1e-9) * (bottom - top));
            float X(int i) => _values.Length < 2 ? 0 : w * i / (_values.Length - 1);

            void HLine(float y, SKColor color, SKPathEffect? effect)
            {
                _hair.Color = color;
                _hair.PathEffect = effect;
                _path.Rewind();
                _path.MoveTo(0, y);
                _path.LineTo(w, y);
                canvas.DrawPath(_path, _hair);
            }

            void VLine(float x, SKColor color, SKPathEffect effect)
            {
                _hair.Color = color;
                _hair.PathEffect = effect;
                _path.Rewind();
                _path.MoveTo(x, 0);
                _path.LineTo(x, h);
                canvas.DrawPath(_path, _hair);
            }

            // Three gridlines, at the labelled values.
            HLine(top, _grid, null);
            HLine((top + bottom) / 2, _grid, null);
            HLine(bottom, _grid, null);
            if (_values.Length < 2) return;

            if (_target is { } t) HLine(Y(t), _faint, _dashed);
            if (_limit is { } l) HLine(Y(l), _alert.WithAlpha(0xB0), _dashed);
            if (_event >= 0 && _event < _values.Length) VLine(X(_event), _alert.WithAlpha(0xB0), _dashed);

            _path.Rewind();
            _path.MoveTo(X(0), Y(_values[0]));
            for (var i = 1; i < _values.Length; i++) _path.LineTo(X(i), Y(_values[i]));

            // The area under the line, closed along the baseline, under a vertical fade.
            if (_gradient is null || _gradientKey != (h, _accent))
            {
                _gradient?.Dispose();
                _gradient = SKShader.CreateLinearGradient(
                    new SKPoint(0, top), new SKPoint(0, bottom),
                    [_accent.WithAlpha(FillTopAlpha), _accent.WithAlpha(0)],
                    SKShaderTileMode.Clamp);
                _gradientKey = (h, _accent);
                _area.Shader = _gradient;
            }
            _fill.Rewind();
            _fill.AddPath(_path);
            _fill.LineTo(X(_values.Length - 1), bottom);
            _fill.LineTo(X(0), bottom);
            _fill.Close();
            canvas.DrawPath(_fill, _area);

            _line.Color = _accent;
            canvas.DrawPath(_path, _line);

            var c = _cursor < 0 || _cursor >= _values.Length ? _values.Length - 1 : _cursor;
            VLine(X(c), _ink, _dotted);
            _dot.Color = _accent;
            canvas.DrawCircle(X(c), Y(_values[c]), 3, _dot);
        }
    }
}
