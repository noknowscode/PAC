using Microsoft.UI.Xaml.Automation;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using static PacConsole.Controls.Charts.ConsoleChartTheme;

namespace PacConsole.Controls.Charts;

/// <summary>Net output, last 48 samples: accent line with a 12% fill, dashed target line.</summary>
public sealed partial class OutputChart : ConsoleChart
{
    private readonly RectangularSection _target = new()
    {
        Yi = 620,
        Yj = 620,
        Stroke = Dashed(TextPrimary, alpha: 180),
    };

    public OutputChart()
    {
        Chart.Series =
        [
            new LineSeries<ObservableValue>
            {
                Values = Points,
                Stroke = Stroke(Accent, 1.5f),
                Fill = Fill(Accent, 31),
                GeometrySize = 0,
                GeometryStroke = null,
                GeometryFill = null,
                LineSmoothness = 0,
                Name = "Net output (MW)",
            },
        ];
        Chart.XAxes = [FixedAxis(0, 47, visible: false)];
        Chart.YAxes = [FixedAxis(480, 700, visible: true, step: 50)];
        Chart.Sections = [_target];
        AutomationProperties.SetName(this, "Net output, last 48 samples");
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
            new ColumnSeries<ObservableValue>
            {
                Values = Points,
                Fill = Fill(Accent, 179),
                Stroke = Stroke(Accent, 0.6f),
                MaxBarWidth = 9,
                Padding = 2,
                IgnoresBarPosition = true,
                Name = "Generation (MWh)",
            },
            new ColumnSeries<ObservableValue>
            {
                Values = _current,
                Fill = Fill(Cyan, 217),
                Stroke = Stroke(TextPrimary, 0.6f),
                MaxBarWidth = 9,
                Padding = 2,
                IgnoresBarPosition = true,
                Name = "Current hour (MWh)",
            },
        ];
        Chart.XAxes = [FixedAxis(-0.5, 23.5, visible: false)];
        Chart.YAxes = [FixedAxis(400, 700, visible: true, step: 150)];
        AutomationProperties.SetName(this, "Hourly generation, last 24 hours");
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
            Stroke = Stroke(Cyan, 1.3f),
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
        Chart.Sections =
        [
            new RectangularSection { Yi = 4.5, Yj = 4.5, Stroke = Dashed(Alert, alpha: 204) },
        ];
        Chart.TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Hidden;
        Chart.DrawMargin = new LiveChartsCore.Measure.Margin(0, 4, 0, 4);
        AutomationProperties.SetName(this, "Vibration trend for the selected unit");
    }

    public bool IsHigh
    {
        get => (bool)GetValue(IsHighProperty);
        set => SetValue(IsHighProperty, value);
    }

    public static readonly DependencyProperty IsHighProperty =
        DependencyProperty.Register(nameof(IsHigh), typeof(bool), typeof(VibrationChart),
            new PropertyMetadata(false, (d, e) =>
                ((VibrationChart)d)._line.Stroke = Stroke((bool)e.NewValue ? AlertText : Cyan, 1.3f)));

    protected override void OnPointsUpdated(IReadOnlyList<double> values)
    {
        if (values.Count > 0)
        {
            AutomationProperties.SetHelpText(this, $"Vibration {values[^1]:0.0} mm/s, limit 4.5");
        }
    }
}
