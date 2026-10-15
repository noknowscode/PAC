using Microsoft.UI.Xaml.Automation;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using static PacConsole.Controls.Charts.ConsoleChartTheme;

namespace PacConsole.Controls.Charts;

/// <summary>Net output, last 48 samples: accent line with a 12% fill, dashed target line.</summary>
public sealed partial class OutputChart : ConsoleChart
{
    private readonly RectangularSection _target = new() { Yi = 620, Yj = 620, Stroke = Dashed(TextPrimary, alpha: 180) };
    private readonly LineSeries<ObservableValue> _line;
    private readonly Axis _axis = FixedAxis(500, 700, visible: true, step: 100);

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
        ];
        Chart.XAxes = [FixedAxis(0, 47, visible: false)];
        Chart.YAxes = [_axis];
        Chart.Sections = [_target];
        AutomationProperties.SetName(this, "Net output, last 48 samples");
    }

    protected override void ApplyTheme()
    {
        Recolor(_target.Stroke, TextPrimary, 180);
        Recolor(_line.Stroke, Accent);
        Recolor(_line.Fill, Accent, 20);
        Repaint(_axis);
    }

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

    protected override void OnPointsUpdated(IReadOnlyList<double> values)
    {
        if (values.Count > 0)
        {
            AutomationProperties.SetHelpText(this, $"Net output {values[^1]:0} MW, target {Target:0} MW");
        }
    }
}

/// <summary>Hourly generation, last 24 hours: accent columns, the current hour in cyan.</summary>
public sealed partial class HourlyChart : ConsoleChart
{
    // Overlay series: null everywhere except the current hour, drawn on top in cyan.
    private readonly System.Collections.ObjectModel.ObservableCollection<ObservableValue> _current = [];

    public HourlyChart()
    {
        Chart.Series =
        [
            _hours = new ColumnSeries<ObservableValue>
            {
                Values = Points,
                Fill = Fill(InkSoft, 64),
                Stroke = null,
                MaxBarWidth = 9,
                Padding = 2,
                IgnoresBarPosition = true,
                Name = "Generation (MWh)",
            },
            _now = new ColumnSeries<ObservableValue>
            {
                Values = _current,
                Fill = Fill(Accent, 255),
                Stroke = null,
                MaxBarWidth = 9,
                Padding = 2,
                IgnoresBarPosition = true,
                Name = "Current hour (MWh)",
            },
        ];
        Chart.XAxes = [FixedAxis(-0.5, 23.5, visible: false)];
        Chart.YAxes = [_axis];
        AutomationProperties.SetName(this, "Hourly generation, last 24 hours");
    }

    private readonly ColumnSeries<ObservableValue> _hours;
    private readonly ColumnSeries<ObservableValue> _now;
    private readonly Axis _axis = FixedAxis(300, 700, visible: true, step: 200);

    protected override void ApplyTheme()
    {
        Recolor(_hours.Fill, InkSoft, 64);
        Recolor(_now.Fill, Accent);
        Repaint(_axis);
    }

    protected override void OnPointsUpdated(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return;
        while (_current.Count < values.Count) _current.Add(new ObservableValue(null));
        while (_current.Count > values.Count) _current.RemoveAt(_current.Count - 1);
        for (var i = 0; i < values.Count; i++)
        {
            double? v = i == values.Count - 1 ? values[i] : null;
            if (_current[i].Value != v) _current[i].Value = v;
        }
        AutomationProperties.SetHelpText(this, $"Current hour {values[^1]:0} MWh, peak {values.Max():0} MWh");
    }
}

/// <summary>Vibration sparkline, last 30 samples, with the 4.5 mm/s limit dashed in coral.</summary>
public sealed partial class VibrationChart : ConsoleChart
{
    private readonly LineSeries<ObservableValue> _line;

    public VibrationChart()
    {
        _line = new LineSeries<ObservableValue>
        {
            Values = Points,
            Stroke = Stroke(InkSoft, 1.3f),
            Fill = null,
            GeometrySize = 0,
            GeometryStroke = null,
            GeometryFill = null,
            LineSmoothness = 0,
            Name = "Vibration (mm/s)",
        };
        Chart.Series = [_line];
        Chart.XAxes = [FixedAxis(0, 29, visible: false)];
        Chart.YAxes = [FixedAxis(0, 6, visible: false)];
        Chart.Sections = [_limit];
        Chart.TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden;
        Chart.DrawMargin = new LiveChartsCore.Measure.Margin(0, 4, 0, 4);
        AutomationProperties.SetName(this, "Vibration trend for the selected unit");
    }

    private readonly RectangularSection _limit = new() { Yi = 4.5, Yj = 4.5, Stroke = Dashed(Alert, alpha: 204) };

    protected override void ApplyTheme()
    {
        Recolor(_limit.Stroke, Alert, 204);
        Recolor(_line.Stroke, IsHigh ? AlertText : InkSoft);
    }

    public bool IsHigh
    {
        get => (bool)GetValue(IsHighProperty);
        set => SetValue(IsHighProperty, value);
    }

    public static readonly DependencyProperty IsHighProperty =
        DependencyProperty.Register(nameof(IsHigh), typeof(bool), typeof(VibrationChart),
            new PropertyMetadata(false, (d, e) =>
                Recolor(((VibrationChart)d)._line.Stroke, (bool)e.NewValue ? AlertText : InkSoft)));

    protected override void OnPointsUpdated(IReadOnlyList<double> values)
    {
        if (values.Count > 0)
        {
            AutomationProperties.SetHelpText(this, $"Vibration {values[^1]:0.0} mm/s, limit 4.5");
        }
    }
}
