using PacConsole.Core.Operations;
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

/// <summary>
/// What a reading is computed from at one tick: the unit's base reading, the plant's net output, the
/// setpoints and the operator's actions.
/// </summary>
public readonly record struct ReadingContext(UnitReading Unit, long Tick, double NetMw, Setpoints Setpoints, PlantOps? Ops = null)
{
    /// <summary>The operator's actions (the initial plant when none were given).</summary>
    public PlantOps O => Ops ?? PlantOps.Initial;

    /// <summary>
    /// A drift round <paramref name="value"/>: ±<paramref name="swing"/> of it, mostly a slow wave
    /// (about 50 min, so an hour's trend reads as drift, not a sine), a faster ripple and a little noise.
    /// </summary>
    public double Around(double value, double swing, double seed) =>
        value * (1 + swing * (0.75 * Math.Sin(Tick * 0.004 + seed) + 0.25 * Math.Sin(Tick * 0.05 + seed * 2))
            + swing * 0.3 * (Simulation.Noise(Tick, seed) - 0.5));

    /// <summary>Cells' worth of cooling missing below design duty at this tick.</summary>
    public double CoolingDeficit => PlantEffects.CoolingDeficit(O, Tick);
}

/// <summary>A reading's scale for its range bar: the display span and the normal band inside it.</summary>
public readonly record struct Band(double Min, double NormalLow, double NormalHigh, double Max);

public enum AlarmPriority { Low, Medium, High }

/// <summary>What to tell the operator when an alarm stands: why it probably happened, what to do, what happens if not.</summary>
public sealed record Guidance(string Cause, ImmutableArray<string> Steps, string Consequence);

/// <summary>
/// A reading that alarms when it passes its limit (<see cref="ReadingSpec.Limit"/>) upward or downward,
/// and clears only once back inside by <see cref="Deadband"/> (ISA-18.2), so a value hovering at the
/// limit does not chatter in and out.
/// </summary>
public sealed record AlarmSpec(AlarmPriority Priority, bool Above, string Title, Guidance Guidance, double Deadband);

/// <summary>
/// One reading an item of equipment reports. <see cref="Limit"/> is drawn on its trend and range bar,
/// and is the alarm limit when <see cref="Alarm"/> is set; <see cref="Target"/> is a reference line.
/// </summary>
public sealed record ReadingSpec(
    string Key,
    string Label,
    string Unit,
    string Format,
    ReadingGroup Group,
    Func<ReadingContext, double> Value,
    Band Band,
    Func<Setpoints, double>? Limit = null,
    Func<Setpoints, double>? Target = null,
    AlarmSpec? Alarm = null);

/// <summary>An item of selectable equipment: its unit, a one-line description, its readings and its connections.</summary>
public sealed record Equipment(UnitId Id, string Description, ImmutableArray<ReadingSpec> Readings, ImmutableArray<UnitId> Connections)
{
    public UnitInfo Info => UnitCatalog.Get(Id);

    /// <summary>"GEN-03 · Generator", as the picker and chips show it.</summary>
    public string Title => $"{Info.Code} · {Info.Name}";
}

/// <summary>
/// The plant's equipment and what each reports. Values are simulated (deterministic in the tick and
/// the operator's actions, see <see cref="Simulation"/> and <see cref="PlantEffects"/>): where the base
/// unit reading carries the quantity (vibration, the turbine's inlet pressure) the spec reads it.
/// </summary>
public static class EquipmentCatalog
{
    /// <summary>Vibration above this, in mm/s, alarms on any unit (ISO 10816 zone C).</summary>
    public const double VibrationLimit = 4.5;

    private static readonly Band VibrationBand = new(0, 0, 2.8, 7.1);

    private static readonly Guidance MachineVibration = new(
        "Imbalance, misalignment or bearing wear.",
        [
            "Compare with the trend: a step change points to a mechanical fault, a slow rise to wear.",
            "Reduce the machine's load if the process allows.",
            "Request a vibration survey; plan a stop if it keeps rising.",
        ],
        "Above 7.1 mm/s (ISO 10816 zone D) damage is likely; protection trips the machine at 11 mm/s.");

    private static ReadingSpec Vibration(string key, string label, string title, Guidance? guidance = null) =>
        new(key, label, "mm/s", "0.0", ReadingGroup.Condition, c => c.Unit.Vib, VibrationBand,
            Limit: _ => VibrationLimit,
            Alarm: new(AlarmPriority.Medium, true, title, guidance ?? MachineVibration, Deadband: 0.5));

    public static readonly ImmutableArray<Equipment> All =
    [
        new(UnitId.Stk, "Flue gas from the boiler leaves through two stacks; the stack damper sets the draught.",
        [
            new("stk.flow", "Flue gas flow", "kNm³/h", "0", ReadingGroup.Overview, c => c.Unit.Flow, new(150, 250, 360, 450)),
            new("stk.temp", "Flue gas temperature", "°C", "0", ReadingGroup.Overview, c => c.Around(142, 0.01, 1), new(100, 130, 155, 180)),
            new("stk.o2", "Oxygen", "%", "0.0", ReadingGroup.Overview, c => c.Around(3.8, 0.04, 2), new(0, 3, 5, 8)),
            new("stk.nox", "NOx", "mg/Nm³", "0", ReadingGroup.Overview, c => c.Around(118, 0.05, 3), new(0, 90, 140, 200),
                Limit: _ => 150,
                Alarm: new(AlarmPriority.Medium, true, "NOx above the permit",
                    new("Excess air too low or burner flame too hot.",
                        ["Raise the stack damper 5 %.", "Check oxygen: aim for 3.5 to 4.5 %.", "Reduce load if NOx stays above 150."],
                        "Sustained exceedance breaches the operating permit."), Deadband: 5)),
            new("stk.damper", "Damper position", "%", "0", ReadingGroup.Overview, c => c.Setpoints.DamperPct, new(0, 40, 90, 100)),
            new("stk.opacity", "Opacity", "%", "0.0", ReadingGroup.Condition, c => c.Around(6, 0.08, 4), new(0, 0, 10, 20)),
            Vibration("stk.vib", "Stack vibration", "Stack vibration high"),
        ], [UnitId.Sil, UnitId.Tka]),

        new(UnitId.Tka, "Fuel oil storage in a bunded farm; the transfer pumps feed the boiler burners.",
        [
            new("tka.levelA", "Tank A level", "%", "0", ReadingGroup.Overview, c => c.Around(68, 0.01, 1), new(0, 20, 90, 100)),
            new("tka.levelB", "Tank B level", "%", "0", ReadingGroup.Overview, c => c.Around(54, 0.01, 2), new(0, 20, 90, 100)),
            new("tka.flow", "Transfer flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow * PlantEffects.FuelTransfer(c.O, c.Tick), new(0, 80, 150, 180),
                Limit: _ => 20,
                Alarm: new(AlarmPriority.Medium, false, "Fuel transfer flow low",
                    new("No transfer pump running, or the suction valve V-108 is shut.",
                        ["Start the other transfer pump.", "Check that V-108 is open.", "Reduce load until flow recovers."],
                        "The burners starve within minutes and the boiler trips on flame failure."), Deadband: 5)),
            new("tka.temp", "Fuel temperature", "°C", "0", ReadingGroup.Overview, c => c.Unit.Temp, new(10, 30, 50, 70)),
            new("tka.press", "Pump discharge pressure", "bar", "0.0", ReadingGroup.Overview,
                c => c.Unit.Press * (0.15 + 0.85 * PlantEffects.FuelTransfer(c.O, c.Tick)), new(0, 5, 8, 10)),
            new("tka.sump", "Bund sump level", "%", "0", ReadingGroup.Condition, c => c.Around(4, 0.2, 3), new(0, 0, 20, 100)),
            Vibration("tka.vib", "Pump vibration", "Transfer pump vibration high"),
        ], [UnitId.Stk]),

        new(UnitId.Gen, "Electrical output and generator thermal condition.",
        [
            new("gen.gross", "Gross generation", "MW", "0", ReadingGroup.Overview, c => c.NetMw + 43, new(400, 600, 700, 760), Target: s => s.TargetMw + 43),
            new("gen.reactive", "Reactive power", "MVAr", "0", ReadingGroup.Overview, c => c.Around(c.O.Reactive.ValueAt(c.Tick), 0.03, 1), new(0, 60, 180, 240)),
            new("gen.voltage", "Terminal voltage", "kV", "0.00", ReadingGroup.Overview, c => c.Around(21, 0.002, 2), new(19, 20.5, 21.5, 23)),
            new("gen.frequency", "Frequency", "Hz", "0.00", ReadingGroup.Overview, c => c.Around(50, 0.0004, 3), new(49.5, 49.9, 50.1, 50.5)),
            new("gen.stator", "Stator winding temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(78, 0.01, 4), new(40, 60, 95, 130)),
            new("gen.pf", "Power factor", "", "0.00", ReadingGroup.Overview, c => c.Around(0.98, 0.002, 5), new(0.8, 0.9, 1, 1)),
            new("gen.h2", "Hydrogen pressure", "bar", "0.0", ReadingGroup.Condition, c => c.Unit.Press, new(2, 3, 3.5, 4)),
            Vibration("gen.vib", "Bearing vibration", "Generator bearing vibration high"),
        ], [UnitId.Trb]),

        new(UnitId.Trb, "High-, intermediate- and low-pressure casings on one shaft, driving GEN-03.",
        [
            new("trb.press", "Inlet pressure", "bar", "0", ReadingGroup.Overview, c => c.Unit.Press, new(120, 150, 170, 190),
                Limit: s => s.TurbineLimitBar,
                Alarm: new(AlarmPriority.High, true, "Turbine inlet pressure above its limit",
                    new("Load rejection, a governor valve sticking open, or the limit set too low.",
                        ["Reduce the load setpoint by 20 MW.", "Check admission valves V-101 to V-103.", "If pressure reaches the limit + 5 bar, trip the turbine."],
                        "Casing stress, and the safety valves lift at 185 bar."), Deadband: 1)),
            new("trb.temp", "Inlet temperature", "°C", "0", ReadingGroup.Overview, c => c.Unit.Temp, new(480, 525, 545, 570)),
            new("trb.flow", "Steam flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow, new(300, 480, 600, 700)),
            new("trb.speed", "Shaft speed", "rpm", "0", ReadingGroup.Overview, c => c.Around(3000, 0.0008, 1), new(2900, 2985, 3015, 3100)),
            new("trb.load", "Load", "%", "0", ReadingGroup.Overview, c => c.Unit.Load, new(0, 50, 95, 110)),
            new("trb.oil", "Lube oil temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(48, 0.01, 2), new(30, 40, 55, 70)),
            new("trb.thrust", "Thrust bearing temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(72, 0.01, 3), new(40, 60, 85, 110)),
            Vibration("trb.vib", "Bearing vibration", "Turbine bearing vibration high"),
        ], [UnitId.Gen, UnitId.Cnd]),

        new(UnitId.Clg, "Mechanical-draft cells that reject the condenser's heat: five fans on duty, one on standby.",
        [
            new("clg.basin", "Basin temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(24, 0.015, 1) + 1.5 * c.CoolingDeficit, new(15, 20, 28, 35)),
            new("clg.return", "Return water temperature", "°C", "0.0", ReadingGroup.Overview, c => c.Around(33, 0.01, 2) + 1.5 * c.CoolingDeficit, new(25, 30, 38, 45)),
            new("clg.flow", "Circulating flow", "m³/h", "#,0", ReadingGroup.Overview, c => c.Around(18400, 0.01, 3), new(12000, 16000, 20000, 24000)),
            new("clg.power", "Fan power", "kW", "0", ReadingGroup.Overview, c => c.Around(124, 0.02, 4) * PlantEffects.CoolingCapacity(c.O, c.Tick)
                * Math.Pow(PlantEffects.FanSpeedFraction(c.O, c.Tick), 2), new(0, 450, 750, 900)),
            new("clg.gearbox", "Gearbox oil temperature", "°C", "0", ReadingGroup.Condition, c => c.Around(61, 0.01, 5), new(30, 45, 75, 95)),
            Vibration("clg.vib", "Fan vibration", "Cooling fan vibration high",
                new("Cell 3's fan reads highest: blade fouling or a loose blade.",
                    [
                        "Start the standby cell 6.",
                        "Stop cell 3 once cell 6 is running.",
                        "Or reduce fan speed to 75 %: the water runs about 2 °C warmer.",
                    ],
                    "A failing fan can shed a blade; lost cooling raises condenser pressure and cuts output.")),
        ], [UnitId.Cnd]),

        new(UnitId.Sil, "Collects furnace ash conveyed pneumatically from the boiler; trucks unload it.",
        [
            new("sil.level", "Silo level", "%", "0", ReadingGroup.Overview, c => PlantEffects.SiloLevel(c.O, c.Tick), new(0, 20, 85, 100),
                Limit: _ => 85,
                Alarm: new(AlarmPriority.Low, true, "Ash silo level high",
                    new("Unloading has not kept up with ash production.",
                        ["Start unloading.", "Call for a truck if none is at the bay."],
                        "At 98 % the conveyors stop and ash backs up into the boiler hoppers."), Deadband: 1)),
            new("sil.rate", "Fill rate", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow, new(0, 20, 60, 80)),
            new("sil.air", "Conveying air pressure", "bar", "0.0", ReadingGroup.Overview, c => c.Unit.Press, new(0, 0.8, 1.3, 2)),
            new("sil.filter", "Vent filter differential", "mbar", "0", ReadingGroup.Condition, c => c.Around(12, 0.05, 2), new(0, 5, 20, 40)),
            Vibration("sil.vib", "Vibration", "Silo vibration high"),
        ], [UnitId.Stk]),

        new(UnitId.Cnd, "Water-cooled condenser under the turbine exhaust; cooling water comes from CLG-05.",
        [
            new("cnd.vacuum", "Absolute pressure", "kPa", "0.0", ReadingGroup.Overview,
                c => PlantEffects.CondenserPressure(c.O, c.Tick, c.Around(9, 0.01, 1)), new(0, 6, 11, 20),
                Limit: _ => 12,
                Alarm: new(AlarmPriority.High, true, "Condenser vacuum low",
                    new("Cooling-water flow lost, or not enough cooling cells running.",
                        ["Start CW pump B if pump A has stopped.", "Check that CLG-05 has five cells running.", "Reduce load until pressure falls below 11 kPa."],
                        "Output falls as back-pressure rises; the turbine trips at 18 kPa."), Deadband: 0.5)),
            new("cnd.flow", "Condensate flow", "t/h", "0", ReadingGroup.Overview, c => c.Unit.Flow, new(300, 450, 600, 700)),
            new("cnd.cwIn", "Cooling water in", "°C", "0.0", ReadingGroup.Overview, c => c.Around(24, 0.015, 2) + 1.5 * c.CoolingDeficit, new(15, 20, 28, 35)),
            new("cnd.cwOut", "Cooling water out", "°C", "0.0", ReadingGroup.Overview,
                c => c.Around(33, 0.01, 3) + 1.5 * c.CoolingDeficit + 8 * (1 - PlantEffects.CoolingWaterFlow(c.O, c.Tick)), new(25, 30, 38, 50)),
            new("cnd.hotwell", "Hotwell level", "%", "0", ReadingGroup.Overview, c => c.Around(55, 0.02, 4), new(0, 40, 70, 100)),
            new("cnd.ttd", "Terminal temperature difference", "°C", "0.0", ReadingGroup.Condition, c => c.Around(4.2, 0.04, 5), new(0, 2, 6, 10)),
            new("cnd.air", "Air ingress", "kg/h", "0", ReadingGroup.Condition, c => c.Around(12, 0.06, 6), new(0, 0, 20, 40)),
            Vibration("cnd.vib", "Pump vibration", "Condensate pump vibration high"),
        ], [UnitId.Trb, UnitId.Clg]),
    ];

    public static Equipment Get(UnitId id) => All.First(e => e.Id == id);

    public static ReadingSpec? Find(string key) => All.SelectMany(e => e.Readings).FirstOrDefault(r => r.Key == key);

    /// <summary>The unit a reading key belongs to.</summary>
    public static UnitId UnitOf(string key) => All.First(e => e.Readings.Any(r => r.Key == key)).Id;
}
