using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

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
    public bool HasAlert { get; init; }
    public bool SelectedIsAlert { get; init; }

    // Data freshness: live, or held by the operator (stale once held for StaleAfterSeconds).
    public bool DataIsLive { get; init; }
    public bool DataIsStale { get; init; }

    public ControlMode Mode { get; init; }
    public bool IsAuto => Mode == ControlMode.Auto;
    public bool IsManual => Mode == ControlMode.Manual;
    public bool IsHold => Mode == ControlMode.Hold;

    public string OutputNow { get; init; } = "";

    /// <summary>Seconds after which held data is called stale.</summary>
    public const int StaleAfterSeconds = 10;

    public static ConsoleView Compose(
        PlantSnapshot snapshot,
        Setpoints setpoints,
        ImmutableArray<Valve> valves,
        ControlMode mode,
        UnitId? requestedUnit,
        DateTimeOffset now,
        ViewInputs? inputs = null)
    {
        var readings = Simulation.Readings(snapshot.Tick, setpoints.DamperPct);
        var alerts = AlertRules.Evaluate(readings, setpoints);
        var alerting = AlertRules.AlertingUnits(alerts);

        // Nothing chosen yet: open on the first alerting unit, the one the operator needs.
        var selected = requestedUnit ?? (alerting.IsEmpty ? UnitId.Trb : alerting[0]);
        var held = mode == ControlMode.Hold;
        var age = Math.Max(0, (int)Math.Round((now - snapshot.At).TotalSeconds));
        var outputNow = Simulation.OutputMw(snapshot.Tick, valves.Count(v => v.Open));

        var view = new ConsoleView
        {
            Selected = selected,
            AlertingUnits = alerting,
            HasAlert = !alerting.IsEmpty,
            SelectedIsAlert = alerting.Contains(selected),
            DataIsLive = !held,
            DataIsStale = held && age >= StaleAfterSeconds,
            Mode = mode,
            OutputNow = Math.Round(outputNow).ToString("0", Inv),
        };
        return WithV3(view, snapshot, setpoints, valves, mode, readings, alerts, outputNow, now, age, inputs ?? ViewInputs.Default);
    }
}
