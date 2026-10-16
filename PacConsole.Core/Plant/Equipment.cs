using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Plant;

/// <summary>Where a reading shows in the inspector.</summary>
public enum ReadingGroup
{
    /// <summary>What the equipment is doing: output, flows, pressures.</summary>
    Overview,

    /// <summary>How the equipment is holding up: vibration, bearing and winding temperatures.</summary>
    Condition,
}

/// <summary>What a reading is computed from at one tick: the unit's base reading, the plant's net output and the setpoints.</summary>
public readonly record struct ReadingContext(UnitReading Unit, long Tick, double NetMw, Setpoints Setpoints)
{
    /// <summary>A slow oscillation round <paramref name="value"/>: ±<paramref name="swing"/> of it, with a little noise.</summary>
    public double Around(double value, double swing, double seed) =>
        value * (1 + swing * Math.Sin(Tick * 0.05 + seed) + swing * 0.3 * (Simulation.Noise(Tick, seed) - 0.5));
}

/// <summary>
/// One reading an item of equipment reports. <see cref="Limit"/> is an alarm limit drawn on its trend
/// (crossing it is an alert only where <see cref="AlertRules"/> says so); <see cref="Target"/> is a
/// reference line.
/// </summary>
public sealed record ReadingSpec(
    string Key,
    string Label,
    string Unit,
    string Format,
    ReadingGroup Group,
    Func<ReadingContext, double> Value,
    Func<Setpoints, double>? Limit = null,
    Func<Setpoints, double>? Target = null);

/// <summary>An item of selectable equipment: its unit, a one-line description, its readings and its connections.</summary>
public sealed record Equipment(UnitId Id, string Description, ImmutableArray<ReadingSpec> Readings, ImmutableArray<UnitId> Connections)
{
    public UnitInfo Info => UnitCatalog.Get(Id);

    /// <summary>"GEN-03 · Generator", as the picker and chips show it.</summary>
    public string Title => $"{Info.Code} · {Info.Name}";
}

/// <summary>
/// The plant's equipment and what each reports. Values are simulated (deterministic in the tick, see
/// <see cref="Simulation"/>): where the base unit reading carries the quantity (vibration, the
/// turbine's inlet pressure) the spec reads it, so alerts and readings agree.
/// </summary>
public static class EquipmentCatalog
{
    public const double VibrationLimit = AlertRules.VibrationLimit;

    private static ReadingSpec Vibration(string key, string label = "Vibration") =>
        new(key, label, "mm/s", "0.0", ReadingGroup.Condition, c => c.Unit.Vib, Limit: _ => VibrationLimit);

    public static readonly ImmutableArray<Equipment> All =
    [
        new(UnitId.Stk, "Flue gas from the boiler leaves through two stacks; the stack damper sets the draught.",
        [
            new("stk.flow", "Flue gas flow", "kNm³/h", "0", ReadingGroup.Overview, c => c.Unit.Flow),
            new("stk.temp", "Flue gas temperature", "°C", "0", ReadingGroup.Overview, c => c.Around(142, 0.01, 1)),
            new("stk.o2", "Oxygen", "%", "0.0", ReadingGroup.Overview, c => c.Around(3.8, 0.04, 2)),
            new("stk.nox", "NOx", "mg/Nm³", "0", ReadingGroup.Overview, c => c.Around(118, 0.05, 3), Limit: _ => 150),
            new("stk.damper", "Damper position", "%", "0", ReadingGroup.Overview, c => c.Setpoints.DamperPct),
            new("stk.opacity", "Opacity", "%", "0.0", ReadingGroup.Condition, c => c.Around(6, 0.08, 4)),
            Vibration("stk.vib", "Stack vibration"),
        ], [UnitId.Sil, UnitId.Tka]),

        new(UnitId.Tka, "Fuel oil storage in a bunded farm; the transfer pump feeds the boiler burners.",
        [
            new("tka.levelA", "Tank A level", "%", "0", ReadingGroup.Overview, c => c.Around(68, 0.01, 1)),
            new("tka.levelB", "Tank B level", "%", "0", ReadingGroup.Overview, c => c.Around(54, 0.01, 2)),
            new("tka.flow", "Transfer flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow),
            new("tka.temp", "Fuel temperature", "°C", "0", ReadingGroup.Overview, c => c.Unit.Temp),
            new("tka.press", "Pump discharge pressure", "bar", "0.0", ReadingGroup.Overview, c => c.Unit.Press),
            new("tka.sump", "Bund sump level", "%", "0", ReadingGroup.Condition, c => c.Around(4, 0.2, 3)),
            Vibration("tka.vib", "Pump vibration"),
        ], [UnitId.Stk]),

        new(UnitId.Gen, "Electrical output and generator thermal condition.",
        [
            new("gen.gross", "Gross generation", "MW", "0", ReadingGroup.Overview, c => c.NetMw + 43, Target: s => s.TargetMw + 43),
            new("gen.reactive", "Reactive power", "MVAr", "0", ReadingGroup.Overview, c => c.Around(120, 0.03, 1)),
            new("gen.voltage", "Terminal voltage", "kV", "0.00", ReadingGroup.Overview, c => c.Around(21, 0.002, 2)),
            new("gen.frequency", "Frequency", "Hz", "0.00", ReadingGroup.Overview, c => c.Around(50, 0.0004, 3)),
            new("gen.stator", "Stator winding temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(78, 0.01, 4)),
            new("gen.pf", "Power factor", "", "0.00", ReadingGroup.Overview, c => c.Around(0.98, 0.002, 5)),
            new("gen.h2", "Hydrogen pressure", "bar", "0.0", ReadingGroup.Condition, c => c.Unit.Press),
            Vibration("gen.vib", "Bearing vibration"),
        ], [UnitId.Trb]),

        new(UnitId.Trb, "High-, intermediate- and low-pressure casings on one shaft, driving GEN-03.",
        [
            new("trb.press", "Inlet pressure", "bar", "0", ReadingGroup.Overview, c => c.Unit.Press, Limit: s => s.TurbineLimitBar),
            new("trb.temp", "Inlet temperature", "°C", "0", ReadingGroup.Overview, c => c.Unit.Temp),
            new("trb.flow", "Steam flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow),
            new("trb.speed", "Shaft speed", "rpm", "0", ReadingGroup.Overview, c => c.Around(3000, 0.0008, 1)),
            new("trb.load", "Load", "%", "0", ReadingGroup.Overview, c => c.Unit.Load),
            new("trb.oil", "Lube oil temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(48, 0.01, 2)),
            new("trb.thrust", "Thrust bearing temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(72, 0.01, 3)),
            Vibration("trb.vib", "Bearing vibration"),
        ], [UnitId.Gen, UnitId.Cnd]),

        new(UnitId.Clg, "Mechanical-draft cells that reject the condenser's heat; six fans over a common basin.",
        [
            new("clg.basin", "Basin temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(24, 0.015, 1)),
            new("clg.return", "Return water temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(33, 0.01, 2)),
            new("clg.flow", "Circulating flow", "m³/h", "#,0", ReadingGroup.Overview, c => c.Around(18400, 0.01, 3)),
            new("clg.power", "Fan power", "kW", "0", ReadingGroup.Overview, c => c.Around(620, 0.02, 4)),
            new("clg.gearbox", "Gearbox oil temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(61, 0.01, 5)),
            Vibration("clg.vib", "Fan vibration"),
        ], [UnitId.Cnd]),

        new(UnitId.Sil, "Collects furnace ash conveyed pneumatically from the boiler.",
        [
            new("sil.level", "Silo level", "%", "0", ReadingGroup.Overview, c => c.Around(62, 0.01, 1)),
            new("sil.rate", "Fill rate", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow),
            new("sil.air", "Conveying air pressure", "bar", "0.0", ReadingGroup.Overview, c => c.Unit.Press),
            new("sil.filter", "Vent filter differential", "mbar", "0", ReadingGroup.Condition, c => c.Around(12, 0.05, 2)),
            Vibration("sil.vib"),
        ], [UnitId.Stk]),

        new(UnitId.Cnd, "Water-cooled condenser under the turbine exhaust; cooling water comes from CLG-05.",
        [
            new("cnd.vacuum", "Absolute pressure", "kPa", "0.0", ReadingGroup.Overview, c => c.Around(9, 0.01, 1)),
            new("cnd.flow", "Condensate flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow),
            new("cnd.cwIn", "Cooling water in", "°C", "0.0", ReadingGroup.Overview, c => c.Around(24, 0.015, 2)),
            new("cnd.cwOut", "Cooling water out", "°C", "0.0", ReadingGroup.Overview, c => c.Around(33, 0.01, 3)),
            new("cnd.hotwell", "Hotwell level", "%", "0", ReadingGroup.Overview, c => c.Around(55, 0.02, 4)),
            new("cnd.ttd", "Terminal temperature difference", "°C", "0.0", ReadingGroup.Condition, c => c.Around(4.2, 0.04, 5)),
            new("cnd.air", "Air ingress", "kg/h", "0", ReadingGroup.Condition, c => c.Around(12, 0.06, 6)),
            Vibration("cnd.vib", "Pump vibration"),
        ], [UnitId.Trb, UnitId.Clg]),
    ];

    public static Equipment Get(UnitId id) => All.First(e => e.Id == id);

    public static ReadingSpec? Find(string key) => All.SelectMany(e => e.Readings).FirstOrDefault(r => r.Key == key);

    /// <summary>The unit a reading key belongs to.</summary>
    public static UnitId UnitOf(string key) => All.First(e => e.Readings.Any(r => r.Key == key)).Id;

    /// <summary>The reading a unit-level alert shows on: vibration on any unit, inlet pressure on the turbine.</summary>
    public static string KeyFor(Alert alert) => alert.Metric == Metric.Press && alert.Unit == UnitId.Trb
        ? "trb.press"
        : Get(alert.Unit).Readings.First(r => r.Unit == "mm/s").Key;
}
