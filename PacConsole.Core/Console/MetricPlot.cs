using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

/// <summary>How a unit's reading is plotted, chosen for what the reading means.</summary>
public enum PlotKind
{
    /// <summary>A trend inside the unit's normal operating band (temperature, flow, pressure).</summary>
    Band,

    /// <summary>A trend against an alarm limit the operator sets (TRB-04 inlet pressure).</summary>
    Limit,

    /// <summary>A trend over ISO 10816 severity zones A, B and C (vibration).</summary>
    Zones,

    /// <summary>A filled share of rated capacity, 0 to 100% (load).</summary>
    Share,

    /// <summary>A narrow-range trend around the design point (efficiency).</summary>
    Design,
}

/// <summary>A horizontal reference on a plot: a band (Low &lt; High) or a line (Low == High).</summary>
public sealed record PlotMark(double Low, double High, string Label);

/// <summary>
/// The focused reading of the selected unit, ready to draw: its last 30 samples, the presentation
/// that suits the metric, the value range and gridline step, and the references to draw behind it.
/// <para>
/// A class, not a record, on purpose: MVUX wraps a record nested in the view in a bindable proxy and
/// updates the proxy in place, so a control taking the whole plot through one dependency property saw
/// the first value and never another (the chart stayed on vibration while its header followed).
/// </para>
/// </summary>
public sealed class MetricPlot(
    Metric metric,
    PlotKind kind,
    string title,
    string unit,
    ImmutableArray<double> series,
    double min,
    double max,
    double step,
    string format,
    ImmutableArray<PlotMark> marks,
    string note,
    bool isHigh)
{
    public Metric Metric { get; } = metric;
    public PlotKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Unit { get; } = unit;
    public ImmutableArray<double> Series { get; } = series;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double Step { get; } = step;
    public string Format { get; } = format;
    public ImmutableArray<PlotMark> Marks { get; } = marks;
    public string Note { get; } = note;
    public bool IsHigh { get; } = isHigh;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The header readout when nothing is hovered: the latest value and its unit.</summary>
    public string Readout => Series.IsEmpty ? "" : $"now {Series[^1].ToString(Format, Inv)} {Unit}".TrimEnd();

    /// <summary>ISO 10816 zone edges for vibration, mm/s (A good, B acceptable, C alarm).</summary>
    public const double ZoneB = 2.8, ZoneC = AlertRules.VibrationLimit, ZoneTop = 7.1;

    public static MetricPlot Compose(UnitId unit, Metric metric, long tick, Setpoints setpoints, bool isHigh)
    {
        var series = Simulation.MetricHistory(unit, metric, tick, setpoints.DamperPct);
        var info = UnitCatalog.Get(unit);
        var title = ConsoleView.LabelOf(metric);
        var u = ConsoleView.UnitOf(metric);

        switch (metric)
        {
            case Metric.Vib:
                return new(metric, PlotKind.Zones, title, u, series, 0, 6, 2, "0.0",
                    [new(0, ZoneB, "A"), new(ZoneB, ZoneC, "B"), new(ZoneC, ZoneTop, "C")],
                    "ISO 10816 zones: A good, B acceptable, C alarm", isHigh);

            case Metric.Load:
                return new(metric, PlotKind.Share, title, u, series, 0, 100, 50, "0", [], "Share of rated capacity", isHigh);

            case Metric.Eff:
            {
                var design = info.BaseEff;
                var (min, max, step) = Range(series, design - 0.6, design + 0.6);
                return new(metric, PlotKind.Design, title, u, series, min, max, step, "0.0",
                    [new(design, design, $"design {design.ToString("0.0", Inv)}")], "Narrow range around the design point", isHigh);
            }

            case Metric.Press when unit == UnitId.Trb:
            {
                var limit = setpoints.TurbineLimitBar;
                var (min, max, step) = Range(series, Math.Min(series.Min(), limit) - 4, Math.Max(series.Max(), limit) + 4);
                return new(metric, PlotKind.Limit, title, u, series, min, max, step, "0",
                    [new(limit, limit, $"limit {limit.ToString("0", Inv)}")], "Alarms above the inlet limit set below", isHigh);
            }

            default:
            {
                var nominal = metric switch
                {
                    Metric.Temp => info.BaseTemp,
                    Metric.Press => info.BasePress,
                    _ => unit == UnitId.Stk ? info.BaseFlow * setpoints.DamperPct / Simulation.ReferenceDamperPct : info.BaseFlow,
                };
                var tolerance = metric switch { Metric.Temp => 0.01, Metric.Press => 0.008, _ => 0.02 };
                var (low, high) = (nominal * (1 - tolerance), nominal * (1 + tolerance));
                var (min, max, step) = Range(series, low, high);
                var format = ConsoleView.FormatMetric(metric, nominal).Contains('.') ? (Math.Abs(nominal) < 1 ? "0.00" : "0.0") : "0";
                var note = unit == UnitId.Stk && metric == Metric.Flow
                    ? $"Normal band scales with the stack damper ({setpoints.DamperPct.ToString("0", Inv)} %)"
                    : "Shaded: normal operating band";
                return new(metric, PlotKind.Band, title, u, series, min, max, step, format, [new(low, high, "normal")], note, isHigh);
            }
        }
    }

    /// <summary>A value range holding the series and the references, padded and split into two steps (three gridlines).</summary>
    private static (double Min, double Max, double Step) Range(ImmutableArray<double> series, double refLow, double refHigh)
    {
        var lo = Math.Min(series.Min(), refLow);
        var hi = Math.Max(series.Max(), refHigh);
        var pad = Math.Max((hi - lo) * 0.15, Math.Abs(hi) * 1e-3);
        lo -= pad;
        hi += pad;
        var step = Nice((hi - lo) / 2);
        var min = Math.Floor(lo / step) * step;
        var max = Math.Ceiling(hi / step) * step;
        return (min, max, step);
    }

    /// <summary>The next 1, 2 or 5 times a power of ten at or above <paramref name="x"/>.</summary>
    private static double Nice(double x)
    {
        var p = Math.Pow(10, Math.Floor(Math.Log10(x)));
        var f = x / p;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p;
    }
}
