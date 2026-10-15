using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using static PacConsole.Controls.Charts.ConsoleChartTheme;

namespace PacConsole.Controls.Charts;

/// <summary>
/// Base for the console's plots, so every chart reads the same way:
/// <list type="bullet">
/// <item>one frame: the same draw margins (plot edges align down the readings column), three
/// hairline gridlines and a time axis that ends at "now";</item>
/// <item>no LiveCharts tooltip (it ignored the theme); pointing at the plot, or arrow keys while
/// it has focus, moves a hairline marker and sets <see cref="Readout"/>, which the section header
/// shows in place of its default text;</item>
/// <item>data steps without tweening (three charts tweening every second stalled viewport frames),
/// but a change of presentation eases over 220 ms (<see cref="Ease"/>).</item>
/// </list>
/// <see cref="Values"/> takes the immutable array MVUX produces; it is copied into the chart's
/// collection in place, so LiveCharts2 updates points instead of rebuilding the series.
/// </summary>
public abstract partial class ConsoleChart : UserControl
{
    protected readonly ObservableCollection<ObservableValue> Points = [];
    protected readonly CartesianChart Chart;

    /// <summary>The hairline at the hovered sample; add it to the chart's sections.</summary>
    protected readonly RectangularSection Marker = new() { IsVisible = false, Stroke = Stroke(Faint, 1) };

    private int? _hover;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _settle;

    protected ConsoleChart()
    {
        Chart = new CartesianChart
        {
            LegendPosition = LegendPosition.Hidden,
            TooltipPosition = TooltipPosition.Hidden,
            AnimationsSpeed = TimeSpan.Zero,
            EasingFunction = EasingFunctions.CubicOut,
            ZoomMode = ZoomAndPanMode.None,
            DrawMargin = FrameMargin,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Content = Chart;
        IsTabStop = true;
        UseSystemFocusVisuals = true;

        // handledEventsToo: the chart's own canvas marks pointer events handled.
        AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) => HoverAt(e.GetCurrentPoint(Chart).Position.X)), true);
        PointerExited += (_, _) => SetHover(null);
        LostFocus += (_, _) => SetHover(null);
        KeyDown += OnKeyDown;

        Loaded += (_, _) => { Styles.ConsoleTheme.Changed += ApplyAll; ApplyAll(); };
        Unloaded += (_, _) => { Styles.ConsoleTheme.Changed -= ApplyAll; _settle?.Stop(); };
    }

    private const int EaseMs = 220;

    private void ApplyAll()
    {
        Recolor(Marker.Stroke, Faint);
        ApplyTheme();
    }

    /// <summary>Recolours the chart's paints from <see cref="ConsoleChartTheme"/>; runs on load and on every theme switch.</summary>
    protected abstract void ApplyTheme();

    /// <summary>What the readout says for one sample, for example "−12 s · 612 MW".</summary>
    protected abstract string Describe(int index, double value);

    /// <summary>Lets the next layout change ease (a new presentation), then returns to stepping.</summary>
    protected void Ease()
    {
        Chart.AnimationsSpeed = TimeSpan.FromMilliseconds(EaseMs);
        // Created on first use: the control's DispatcherQueue is not there yet in its constructor.
        if (_settle is null)
        {
            _settle = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _settle.Interval = TimeSpan.FromMilliseconds(EaseMs + 120);
            _settle.IsRepeating = false;
            _settle.Tick += (_, _) => Chart.AnimationsSpeed = TimeSpan.Zero;
        }
        _settle.Stop();
        _settle.Start();
    }

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(object), typeof(ConsoleChart),
            new PropertyMetadata(null, (d, e) => ((ConsoleChart)d).OnValuesChanged(e.NewValue as IEnumerable<double>)));

    /// <summary>The header text when nothing is hovered (bound from the view).</summary>
    public string DefaultReadout
    {
        get => (string)GetValue(DefaultReadoutProperty);
        set => SetValue(DefaultReadoutProperty, value);
    }

    public static readonly DependencyProperty DefaultReadoutProperty =
        DependencyProperty.Register(nameof(DefaultReadout), typeof(string), typeof(ConsoleChart),
            new PropertyMetadata("", (d, _) => ((ConsoleChart)d).UpdateReadout()));

    /// <summary>The hovered sample's description, or <see cref="DefaultReadout"/>. The section header binds to it.</summary>
    public string Readout
    {
        get => (string)GetValue(ReadoutProperty);
        private set => SetValue(ReadoutProperty, value);
    }

    public static readonly DependencyProperty ReadoutProperty =
        DependencyProperty.Register(nameof(Readout), typeof(string), typeof(ConsoleChart), new PropertyMetadata(""));

    /// <summary>Copies new values into the observable points, touching only those that changed.</summary>
    private void OnValuesChanged(IEnumerable<double>? values)
    {
        var list = values?.ToList() ?? [];
        while (Points.Count > list.Count) Points.RemoveAt(Points.Count - 1);
        for (var i = 0; i < list.Count; i++)
        {
            if (i < Points.Count)
            {
                if (Points[i].Value != list[i]) Points[i].Value = list[i];
            }
            else
            {
                Points.Add(new ObservableValue(list[i]));
            }
        }
        OnPointsUpdated(list);
        UpdateReadout();
    }

    protected virtual void OnPointsUpdated(IReadOnlyList<double> values) { }

    /// <summary>The samples the marker and readout walk: <see cref="Points"/> unless the chart shows another series.</summary>
    protected virtual IReadOnlyList<ObservableValue> HoverPoints => Points;

    /// <summary>Clears the marker and re-reads the readout (after the shown series changes).</summary>
    protected void ResetHover()
    {
        _hover = null;
        Marker.IsVisible = false;
        UpdateReadout();
    }

    private void HoverAt(double x)
    {
        if (HoverPoints.Count == 0) return;
        var data = Chart.ScalePixelsToData(new LvcPointD(x, 0));
        SetHover(Math.Clamp((int)Math.Round(data.X), 0, HoverPoints.Count - 1));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (HoverPoints.Count == 0) return;
        var last = HoverPoints.Count - 1;
        int? next = e.Key switch
        {
            Windows.System.VirtualKey.Left => Math.Max(0, (_hover ?? last + 1) - 1),
            Windows.System.VirtualKey.Right => Math.Min(last, (_hover ?? last - 1) + 1),
            Windows.System.VirtualKey.Home => 0,
            Windows.System.VirtualKey.End => last,
            Windows.System.VirtualKey.Escape => null,
            _ => -1,
        };
        if (next == -1) return;
        SetHover(next);
        e.Handled = true;
    }

    private void SetHover(int? index)
    {
        if (_hover == index) return;
        _hover = index;
        Marker.IsVisible = index is not null;
        if (index is { } i) Marker.Xi = Marker.Xj = i;
        UpdateReadout();
    }

    /// <summary>The header text when nothing is hovered.</summary>
    protected virtual string FallbackReadout => DefaultReadout ?? "";

    protected void UpdateReadout()
    {
        var text = _hover is { } i && i < HoverPoints.Count && HoverPoints[i].Value is { } v ? Describe(i, v) : FallbackReadout;
        if (Readout != text) Readout = text;
        AutomationProperties.SetHelpText(this, text);
    }

    /// <summary>"−12 s" for a sample <paramref name="ago"/> steps before the newest, "now" for the newest.</summary>
    protected static string Ago(int ago, string unit) => ago == 0 ? "now" : $"−{ago} {unit}";
}
