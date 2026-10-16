using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

public sealed record UnitTileView(UnitId Id, string ShortCode, string Subtitle, bool IsSelected, bool IsAlert, string AutomationName)
{
    /// <summary>"selected", "alert" or "normal": one key the view maps to brushes.</summary>
    public string Tone => IsSelected ? "selected" : IsAlert ? "alert" : "normal";
}

public sealed record MetricTileView(string Label, string Value, string Unit, string Status, bool IsHigh, bool IsHeld)
{
    /// <summary>"high", "held" or "normal".</summary>
    public string Tone => IsHigh ? "high" : IsHeld ? "held" : "normal";

    /// <summary>The reading this tile shows; selecting the tile plots it.</summary>
    public Metric Metric { get; init; }

    /// <summary>True for the reading currently plotted.</summary>
    public bool IsFocused { get; init; }

    /// <summary>"focused" or "normal": the tile's surface.</summary>
    public string Surface => IsFocused ? "focused" : "normal";

    public string AutomationName => $"{Label} {Value} {Unit}{(IsHigh ? ", high" : "")}{(IsFocused ? ", plotted" : "")}";
}

public sealed record ValveView(int Id, string Number, bool Open, string State, string AutomationName);

/// <summary>
/// Everything the console page shows at one moment, already formatted. Built by one pure
/// function from the snapshot and the operator's inputs, so the page only binds and the rules
/// (which unit is selected, what is an alert, what "held" looks like) are unit-testable.
/// </summary>
public sealed partial record ConsoleView
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public UnitId Selected { get; init; }
    public ImmutableArray<UnitId> AlertingUnits { get; init; } = [];

    // Header
    public string AlertText { get; init; } = "";
    public bool HasAlert { get; init; }
    public string Clock { get; init; } = "";
    public string DataText { get; init; } = "";
    public bool DataIsLive { get; init; }
    public bool DataIsStale { get; init; }

    /// <summary>"live", "held" or "stale" (held for 10 s or more).</summary>
    public string DataTone => DataIsStale ? "stale" : DataIsLive ? "live" : "held";

    // Control panel
    public ImmutableArray<UnitTileView> Units { get; init; } = [];
    public ControlMode Mode { get; init; }
    public bool IsAuto => Mode == ControlMode.Auto;
    public bool IsManual => Mode == ControlMode.Manual;
    public bool IsHold => Mode == ControlMode.Hold;
    public string ModeText => Mode switch { ControlMode.Manual => "Manual", ControlMode.Hold => "Hold", _ => "Auto" };

    // Selected unit readout
    public string SelectedCode { get; init; } = "";
    public string SelectedName { get; init; } = "";
    public string SelectedStatus { get; init; } = "";
    public string SelectedStatusLine { get; init; } = "";
    public bool SelectedIsAlert { get; init; }
    public string SelectedUptime { get; init; } = "";
    public string SelectedService { get; init; } = "";

    // Telemetry
    public ImmutableArray<MetricTileView> Tiles { get; init; } = [];

    // Each reading and unit by position, for buttons that stay put: MVUX updates these nested records
    // in place, where a repeater over a new array each second rebinds (and recycles) its buttons, and
    // a press could land on a button just rebound to another item.
    public MetricTileView? TempTile => TileAt(0);
    public MetricTileView? PressTile => TileAt(1);
    public MetricTileView? FlowTile => TileAt(2);
    public MetricTileView? LoadTile => TileAt(3);
    public MetricTileView? EffTile => TileAt(4);
    public MetricTileView? VibTile => TileAt(5);
    public UnitTileView? StkTile => UnitAt(0);
    public UnitTileView? TkaTile => UnitAt(1);
    public UnitTileView? GenTile => UnitAt(2);
    public UnitTileView? TrbTile => UnitAt(3);
    public UnitTileView? CndTile => UnitAt(4);
    public UnitTileView? SilTile => UnitAt(5);

    public ValveView? Valve1 => ValveAt(0);
    public ValveView? Valve2 => ValveAt(1);
    public ValveView? Valve3 => ValveAt(2);
    public ValveView? Valve4 => ValveAt(3);
    public ValveView? Valve5 => ValveAt(4);
    public ValveView? Valve6 => ValveAt(5);
    public ValveView? Valve7 => ValveAt(6);
    public ValveView? Valve8 => ValveAt(7);
    public ValveView? Valve9 => ValveAt(8);

    private ValveView? ValveAt(int i) => i < Valves.Length ? Valves[i] : null;
    private MetricTileView? TileAt(int i) => i < Tiles.Length ? Tiles[i] : null;
    private UnitTileView? UnitAt(int i) => i < Units.Length ? Units[i] : null;

    public ImmutableArray<double> OutputSeries { get; init; } = [];
    public string OutputNow { get; init; } = "";
    public string OutputDelta { get; init; } = "";
    public double TargetMw { get; init; }
    public string LiveText { get; init; } = "";

    public ImmutableArray<double> HourlySeries { get; init; } = [];
    public string HourlyPeak { get; init; } = "";

    /// <summary>The hourly plot's header text when nothing is hovered.</summary>
    public string HourlyText => $"peak {HourlyPeak} MWh";

    /// <summary>The focused reading of the selected unit, drawn in the presentation that suits it.</summary>
    public MetricPlot? Plot { get; init; }

    public ImmutableArray<double> VibrationSeries { get; init; } = [];
    public string VibrationNow { get; init; } = "";
    public bool VibrationIsHigh { get; init; }
    public string VibrationStatus { get; init; } = "";
    public string VibrationZone { get; init; } = "";

    public ImmutableArray<ValveView> Valves { get; init; } = [];
    public string OpenValvesText { get; init; } = "";

    /// <summary>Seconds after which held data is shown as stale (coral).</summary>
    public const int StaleAfterSeconds = 10;

    public static ConsoleView Compose(
        PlantSnapshot snapshot,
        Setpoints setpoints,
        ImmutableArray<Valve> valves,
        ControlMode mode,
        UnitId? requestedUnit,
        DateTimeOffset now,
        Metric focus = Metric.Vib,
        ViewInputs? inputs = null)
    {
        var readings = Simulation.Readings(snapshot.Tick, setpoints.DamperPct);
        var alerts = AlertRules.Evaluate(readings, setpoints);
        var alerting = AlertRules.AlertingUnits(alerts);

        // Nothing chosen yet: open on the first alerting unit, the one the operator needs.
        var selected = requestedUnit ?? (alerting.IsEmpty ? UnitId.Trb : alerting[0]);
        var info = UnitCatalog.Get(selected);
        var reading = readings.First(r => r.Id == selected);
        var held = mode == ControlMode.Hold;
        var selectedIsAlert = alerting.Contains(selected);

        var age = Math.Max(0, (int)Math.Round((now - snapshot.At).TotalSeconds));
        var openValves = valves.Count(v => v.Open);
        var output = Simulation.OutputHistory(snapshot.Tick, openValves);
        var outputNow = output[^1];
        var delta = (int)Math.Round(outputNow - setpoints.TargetMw);
        var hourly = Simulation.HourlyGeneration(snapshot.Tick);
        var vibSeries = Simulation.VibrationHistory(selected, snapshot.Tick);
        var vibHigh = reading.Vib > AlertRules.VibrationLimit;

        string AlertTextFor() => alerting.IsEmpty
            ? "All units nominal"
            : $"{alerting.Length} alert{(alerting.Length > 1 ? "s" : "")} · {string.Join(", ", alerting.Select(u => UnitCatalog.Get(u).Code))}";

        var view = new ConsoleView
        {
            Selected = selected,
            AlertingUnits = alerting,

            AlertText = AlertTextFor(),
            HasAlert = !alerting.IsEmpty,
            Clock = now.ToString("HH:mm:ss", Inv),
            DataText = held ? $"Held {age} s" : "Live",
            DataIsLive = !held,
            DataIsStale = held && age >= StaleAfterSeconds,

            Units = [.. UnitCatalog.All.Select(u =>
            {
                var isAlert = alerting.Contains(u.Id);
                return new UnitTileView(
                    u.Id,
                    u.ShortCode,
                    $"{u.Index}{(isAlert ? " · alert" : "")}",
                    u.Id == selected,
                    isAlert,
                    $"{u.Code} {u.Name}{(isAlert ? ", alert" : "")}");
            })],
            Mode = mode,

            SelectedCode = info.Code,
            SelectedName = info.Name,
            SelectedStatus = selectedIsAlert ? "Alert" : "Nominal",
            SelectedStatusLine = (selectedIsAlert ? "Alert" : "Nominal") + (held ? " · held" : ""),
            SelectedIsAlert = selectedIsAlert,
            SelectedUptime = info.Uptime,
            SelectedService = info.LastService,

            Tiles = [.. Enum.GetValues<Metric>().Select(m =>
            {
                var high = alerts.Any(a => a.Unit == selected && a.Metric == m);
                return new MetricTileView(
                    LabelOf(m),
                    FormatMetric(m, reading.Get(m)),
                    UnitOf(m),
                    high ? "high" : "",
                    high,
                    held && !high) { Metric = m, IsFocused = m == focus };
            })],
            Plot = MetricPlot.Compose(selected, focus, snapshot.Tick, setpoints, alerts.Any(a => a.Unit == selected && a.Metric == focus)),

            OutputSeries = output,
            OutputNow = Math.Round(outputNow).ToString("0", Inv),
            OutputDelta = $"{(delta > 0 ? "+" : delta < 0 ? "−" : "±")}{Math.Abs(delta)} MW vs target {setpoints.TargetMw.ToString("0", Inv)} MW",
            TargetMw = setpoints.TargetMw,
            LiveText = held ? "Held" : "Live",

            HourlySeries = hourly,
            HourlyPeak = Math.Round(hourly.Max()).ToString("0", Inv),

            VibrationSeries = vibSeries,
            VibrationNow = reading.Vib.ToString("0.0", Inv),
            VibrationIsHigh = vibHigh,
            VibrationStatus = vibHigh ? "High" : "Normal",
            VibrationZone = vibHigh ? "Zone C" : reading.Vib > 2.8 ? "Zone B" : "Zone A",

            Valves = [.. valves.Select(v => new ValveView(
                v.Id,
                v.Id.ToString(Inv),
                v.Open,
                v.Open ? "open" : "shut",
                $"Valve {v.Code} {(v.Open ? "open" : "shut")}"))],
            OpenValvesText = $"{openValves} of {valves.Length} open",
        };
        return WithV3(view, snapshot, setpoints, valves, mode, readings, alerts, outputNow, now, inputs ?? ViewInputs.Default);
    }

    public static string FormatMetric(Metric metric, double value) => metric switch
    {
        Metric.Temp or Metric.Load => value.ToString("0", Inv),
        Metric.Flow => Math.Round(value).ToString("#,0", Inv),
        Metric.Eff or Metric.Vib => value.ToString("0.0", Inv),
        Metric.Press => Math.Abs(value) < 1 ? value.ToString("0.00", Inv)
            : Math.Abs(value) < 10 ? value.ToString("0.0", Inv)
            : Math.Round(value).ToString("#,0", Inv),
        _ => value.ToString(Inv),
    };

    /// <summary>The reading's name as a datasheet prints it: a word, not an abbreviation.</summary>
    public static string LabelOf(Metric metric) => metric switch
    {
        Metric.Temp => "Temperature",
        Metric.Press => "Pressure",
        Metric.Flow => "Flow",
        Metric.Load => "Load",
        Metric.Eff => "Efficiency",
        Metric.Vib => "Vibration",
        _ => metric.ToString(),
    };

    public static string UnitOf(Metric metric) => metric switch
    {
        Metric.Temp => "°C",
        Metric.Press => "bar",
        Metric.Flow => "t/h",
        Metric.Load or Metric.Eff => "%",
        Metric.Vib => "mm/s",
        _ => "",
    };
}
