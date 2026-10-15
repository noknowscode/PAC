using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml.Automation;
using PacConsole.Core.Console;
using static PacConsole.Controls.Charts.ConsoleChartTheme;

namespace PacConsole.Controls.Charts;

/// <summary>
/// Plant output in one plot with two spans (<see cref="ShowDay"/>): the last 48 seconds as the
/// live line in orange over a faint fill with the target dashed, or the last 24 hours as soft grey
/// columns with the current hour in orange. Both series live in this one chart and the span only
/// shows one or the other: a collapsed chart made LiveCharts dispose its paints, which blanked
/// every chart on the page.
/// </summary>
public sealed partial class OutputChart : ConsoleChart
{
    private readonly System.Collections.ObjectModel.ObservableCollection<ObservableValue> _day = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<ObservableValue> _current = [];
    private readonly RectangularSection _target = new() { Yi = 620, Yj = 620, Stroke = Dashed(TextPrimary, alpha: 160) };
    private readonly LineSeries<ObservableValue> _line;
    private readonly ColumnSeries<ObservableValue> _hours;
    private readonly ColumnSeries<ObservableValue> _now;
    private readonly Axis _axis = FixedAxis(500, 700, visible: true, step: 100);
    private readonly Axis _time = TimeAxis(48, 12, "s");

    public OutputChart()
    {
        Chart.Series =
        [
            _line = new LineSeries<ObservableValue>
            {
                Values = Points,
                Stroke = Stroke(Accent, 1.5f),
                Fill = Fill(Accent, 20),
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0,
                Name = "Net output (MW)",
            },
            _hours = new ColumnSeries<ObservableValue>
            {
                Values = _day,
                Fill = Fill(InkSoft, 64),
                Stroke = null,
                MaxBarWidth = 10,
                Padding = 2,
                IgnoresBarPosition = true,
                IsVisible = false,
                Name = "Generation (MWh)",
            },
            _now = new ColumnSeries<ObservableValue>
            {
                Values = _current,
                Fill = Fill(Accent, 255),
                Stroke = null,
                MaxBarWidth = 10,
                Padding = 2,
                IgnoresBarPosition = true,
                IsVisible = false,
                Name = "Current hour (MWh)",
            },
        ];
        Chart.XAxes = [_time];
        Chart.YAxes = [_axis];
        Chart.Sections = [_target, Marker];
        Describe();
    }

    protected override void ApplyTheme()
    {
        Recolor(_target.Stroke, TextPrimary, 160);
        Recolor(_line.Stroke, Accent);
        Recolor(_line.Fill, Accent, 20);
        Recolor(_hours.Fill, InkSoft, 64);
        Recolor(_now.Fill, Accent);
        Repaint(_axis);
        Repaint(_time);
    }

    protected override IReadOnlyList<ObservableValue> HoverPoints => ShowDay ? _day : Points;

    protected override string FallbackReadout => ShowDay ? DayReadout ?? "" : DefaultReadout ?? "";

    /// <summary>The header text for the 24-hour span when nothing is hovered.</summary>
    public string DayReadout
    {
        get => (string)GetValue(DayReadoutProperty);
        set => SetValue(DayReadoutProperty, value);
    }

    public static readonly DependencyProperty DayReadoutProperty =
        DependencyProperty.Register(nameof(DayReadout), typeof(string), typeof(OutputChart),
            new PropertyMetadata("", (d, _) => ((OutputChart)d).UpdateReadout()));

    protected override string Describe(int index, double value) => ShowDay
        ? $"{Ago(_day.Count - 1 - index, "h")} · {value.ToString("0", CultureInfo.InvariantCulture)} MWh"
        : $"{Ago(Points.Count - 1 - index, "s")} · {value.ToString("0", CultureInfo.InvariantCulture)} MW";

    private void Describe() => AutomationProperties.SetName(this, ShowDay
        ? "Hourly generation, last 24 hours. Arrow keys read past hours."
        : "Net output, last 48 seconds. Arrow keys read past values.");

    public double Target
    {
        get => (double)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(double), typeof(OutputChart),
            new PropertyMetadata(620d, (d, e) =>
            {
                var chart = (OutputChart)d;
                chart._target.Yi = chart._target.Yj = (double)e.NewValue;
            }));

    /// <summary>The last 24 hours of generation, MWh; the newest is the current hour.</summary>
    public IReadOnlyList<double>? DayValues
    {
        get => (IReadOnlyList<double>?)GetValue(DayValuesProperty);
        set => SetValue(DayValuesProperty, value);
    }

    public static readonly DependencyProperty DayValuesProperty =
        DependencyProperty.Register(nameof(DayValues), typeof(object), typeof(OutputChart),
            new PropertyMetadata(null, (d, e) => ((OutputChart)d).OnDayValues(e.NewValue as IEnumerable<double>)));

    private void OnDayValues(IEnumerable<double>? values)
    {
        var list = values?.ToList() ?? [];
        Sync(_day, list.Select(v => (double?)v).ToList());
        Sync(_current, list.Select((v, i) => i == list.Count - 1 ? (double?)v : null).ToList());
    }

    private static void Sync(System.Collections.ObjectModel.ObservableCollection<ObservableValue> target, List<double?> values)
    {
        while (target.Count > values.Count) target.RemoveAt(target.Count - 1);
        for (var i = 0; i < values.Count; i++)
        {
            if (i < target.Count) { if (target[i].Value != values[i]) target[i].Value = values[i]; }
            else target.Add(new ObservableValue(values[i]));
        }
    }

    /// <summary>True for the 24-hour columns, false for the 48-second line.</summary>
    public bool ShowDay
    {
        get => (bool)GetValue(ShowDayProperty);
        set => SetValue(ShowDayProperty, value);
    }

    public static readonly DependencyProperty ShowDayProperty =
        DependencyProperty.Register(nameof(ShowDay), typeof(bool), typeof(OutputChart),
            new PropertyMetadata(false, (d, e) => ((OutputChart)d).ApplySpan((bool)e.NewValue)));

    private void ApplySpan(bool day)
    {
        Ease();
        _line.IsVisible = !day;
        _target.IsVisible = !day;
        _hours.IsVisible = _now.IsVisible = day;
        (_axis.MinLimit, _axis.MaxLimit, _axis.MinStep) = day ? (300d, 700d, 200d) : (500d, 700d, 100d);
        var (count, every, unit) = day ? (24, 6, "h") : (48, 12, "s");
        _time.MinLimit = day ? -0.5 : 0;
        _time.MaxLimit = day ? count - 0.5 : count - 1;
        _time.Labeler = v =>
        {
            var i = (int)Math.Round(v);
            var ago = count - 1 - i;
            if (Math.Abs(v - i) > 0.01 || ago < 0 || ago % every != 0) return "";
            return Ago(ago, unit);
        };
        Describe();
        ResetHover();
    }
}

/// <summary>
/// The selected unit's focused reading, last 30 seconds, drawn the way the metric reads best
/// (<see cref="PlotKind"/>): inside its normal band, against an alarm limit, over ISO severity
/// zones, as a share of capacity, or close around its design point. Every series, section and
/// paint exists once and is shown, hidden or recoloured in place: replacing a paint on a drawn
/// chart disposes LiveCharts' shared label typeface and freezes the chart.
/// </summary>
public sealed partial class MetricChart : ConsoleChart
{
    private readonly LineSeries<ObservableValue> _line;
    private readonly Axis _axis = FixedAxis(0, 6, visible: true, step: 2);
    private readonly Axis _time = TimeAxis(30, 10, "s");
    private readonly RectangularSection _band = new() { Fill = Fill(InkSoft, 22) };
    private readonly RectangularSection _limit = new() { Stroke = Dashed(Alert, 220, 1.2f) };
    private readonly RectangularSection _design = new() { Stroke = Dashed(TextPrimary, 150) };
    private readonly RectangularSection _zoneB = new() { Fill = Fill(InkSoft, 18) };
    private readonly RectangularSection _zoneC = new() { Fill = Fill(Alert, 26) };
    private PlotKind? _kind;
    private string _unit = "";
    private string _format = "0.0";
    private bool _isHigh;

    public MetricChart()
    {
        _line = new LineSeries<ObservableValue>
        {
            Values = Points,
            Stroke = Stroke(InkSoft, 1.5f),
            Fill = Fill(InkSoft, 0),
            GeometrySize = 0,
            GeometryStroke = null,
            GeometryFill = null,
            LineSmoothness = 0,
            Name = "Reading",
        };
        Chart.Series = [_line];
        Chart.XAxes = [_time];
        Chart.YAxes = [_axis];
        Chart.Sections = [_zoneB, _zoneC, _band, _design, _limit, Marker];
        AutomationProperties.SetName(this, "Selected reading, last 30 seconds. Arrow keys read past values.");
    }

    public MetricPlot? Plot
    {
        get => (MetricPlot?)GetValue(PlotProperty);
        set => SetValue(PlotProperty, value);
    }

    public static readonly DependencyProperty PlotProperty =
        DependencyProperty.Register(nameof(Plot), typeof(MetricPlot), typeof(MetricChart),
            new PropertyMetadata(null, (d, e) => ((MetricChart)d).Apply(e.NewValue as MetricPlot)));

    private void Apply(MetricPlot? plot)
    {
        if (plot is null) return;
        // A new presentation (another metric, or a unit whose reading reads differently) eases into
        // place; the once-a-second data steps do not.
        var changed = _kind != plot.Kind || _axis.MinLimit != plot.Min || _axis.MaxLimit != plot.Max;
        if (_kind is not null && changed) Ease();
        _kind = plot.Kind;

        _axis.MinLimit = plot.Min;
        _axis.MaxLimit = plot.Max;
        _axis.MinStep = plot.Step;
        // Axis labels carry the step's decimals (0, 2, 4, 6), not the reading's (0.0, 2.0, ...).
        var decimals = Math.Max(0, (int)-Math.Floor(Math.Log10(plot.Step) + 1e-9));
        var axisFormat = decimals == 0 ? "0" : "0." + new string('0', decimals);
        _axis.Labeler = v => v.ToString(axisFormat, CultureInfo.InvariantCulture);

        _band.IsVisible = plot.Kind == PlotKind.Band;
        _limit.IsVisible = plot.Kind == PlotKind.Limit;
        _design.IsVisible = plot.Kind == PlotKind.Design;
        _zoneB.IsVisible = _zoneC.IsVisible = plot.Kind == PlotKind.Zones;
        if (plot.Kind == PlotKind.Band) (_band.Yi, _band.Yj) = (plot.Marks[0].Low, plot.Marks[0].High);
        if (plot.Kind == PlotKind.Limit) _limit.Yi = _limit.Yj = plot.Marks[0].Low;
        if (plot.Kind == PlotKind.Design) _design.Yi = _design.Yj = plot.Marks[0].Low;
        if (plot.Kind == PlotKind.Zones)
        {
            (_zoneB.Yi, _zoneB.Yj) = (plot.Marks[1].Low, plot.Marks[1].High);
            (_zoneC.Yi, _zoneC.Yj) = (plot.Marks[2].Low, plot.Marks[2].High);
        }

        _unit = plot.Unit;
        _format = plot.Format;
        _isHigh = plot.IsHigh;
        ApplyTheme();
        Values = plot.Series;
    }

    protected override void ApplyTheme()
    {
        // A reading over its limit is drawn in the alert colour; a share of capacity is filled.
        var ink = _isHigh ? AlertText : InkSoft;
        Recolor(_line.Stroke, ink);
        Recolor(_line.Fill, ink, _kind == PlotKind.Share ? (byte)40 : (byte)0);
        Recolor(_band.Fill, InkSoft, 22);
        Recolor(_limit.Stroke, Alert, 220);
        Recolor(_design.Stroke, TextPrimary, 150);
        Recolor(_zoneB.Fill, InkSoft, 18);
        Recolor(_zoneC.Fill, Alert, 26);
        Repaint(_axis);
        Repaint(_time);
    }

    protected override string Describe(int index, double value) =>
        $"{Ago(Points.Count - 1 - index, "s")} · {value.ToString(_format, CultureInfo.InvariantCulture)} {_unit}".TrimEnd();
}
