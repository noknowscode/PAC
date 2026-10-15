using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;

namespace PacConsole.Controls.Charts;

/// <summary>
/// Base for the three console charts: a CartesianChart with no legend, fixed axes, and an
/// observable point collection. <see cref="Values"/> takes the immutable array MVUX produces;
/// the adapter copies it into the chart's collection in place, so LiveCharts2 animates the
/// change instead of rebuilding the series.
/// </summary>
public abstract partial class ConsoleChart : UserControl
{
    protected readonly ObservableCollection<ObservableValue> Points = [];
    protected readonly CartesianChart Chart;

    protected ConsoleChart()
    {
        Chart = new CartesianChart
        {
            LegendPosition = LegendPosition.Hidden,
            TooltipPosition = TooltipPosition.Top,
            // No tween on data updates: three charts animating 300 ms of every second stalled about
            // one viewport frame in ten (measured p90 45 ms). Values step once a second instead.
            AnimationsSpeed = TimeSpan.Zero,
            ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.None,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        Content = Chart;
        Loaded += (_, _) => { Styles.ConsoleTheme.Changed += ApplyTheme; ApplyTheme(); };
        Unloaded += (_, _) => Styles.ConsoleTheme.Changed -= ApplyTheme;
    }

    /// <summary>Builds the chart's paints from <see cref="ConsoleChartTheme"/>; runs on load and on every theme switch.</summary>
    protected abstract void ApplyTheme();

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(object), typeof(ConsoleChart),
            new PropertyMetadata(null, (d, e) => ((ConsoleChart)d).OnValuesChanged(e.NewValue as IEnumerable<double>)));

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
    }

    protected virtual void OnPointsUpdated(IReadOnlyList<double> values) { }
}
