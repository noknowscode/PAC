using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Operations;

/// <summary>
/// How the operator's actions move the plant, as pure functions of the ops state and the tick, so a
/// replay of past ticks agrees with what the operator saw. Changes settle exponentially from the
/// tick they were commanded; before that tick a device is taken to have been in its other state.
/// </summary>
public static class PlantEffects
{
    /// <summary>Time constants in ticks (2 s): a fan cell spinning down, a CW pump's effect on vacuum, the fuel line.</summary>
    public const double FanTau = 10, VacuumTau = 30, FuelTau = 5;

    /// <summary>How much a device is running at <paramref name="tick"/>: 1 running, 0 stopped, between while it settles.</summary>
    public static double RunningFactor(Device d, long tick, double tau)
    {
        if (d.ChangedTick <= 0) return d.Running ? 1 : 0;
        if (tick < d.ChangedTick) return d.Running ? 0 : 1;
        var settled = 1 - Math.Exp(-(tick - d.ChangedTick) / tau);
        return d.Running ? settled : 1 - settled;
    }

    // ---------------------------------------------------------------- CLG-05 cooling bank

    public static double FanSpeedFraction(PlantOps ops, long tick) => ops.FanSpeed.ValueAt(tick) / 100;

    /// <summary>Running cells × fan speed: 5 is the design duty (five cells at full speed).</summary>
    public static double CoolingCapacity(PlantOps ops, long tick) =>
        ops.RunningSum(UnitId.Clg, tick, FanTau) * FanSpeedFraction(ops, tick);

    /// <summary>Cells' worth of cooling missing below design duty; each warms the water 1.5 °C.</summary>
    public static double CoolingDeficit(PlantOps ops, long tick) => Math.Max(0, 5 - CoolingCapacity(ops, tick));

    /// <summary>A healthy fan's vibration: about 2 mm/s with a little wander.</summary>
    public static double HealthyFanVibration(long tick) =>
        2.0 + 0.12 * Math.Sin(tick * 0.9 + 4) + (Simulation.Noise(tick, 11) - 0.5) * 0.1;

    /// <summary>
    /// The bank's vibration: the worst cell. Cell 3 carries the fault (the base drift in
    /// <see cref="Simulation.Vibration"/>); its excess over a healthy fan scales with speed squared
    /// and fades as the cell stops.
    /// </summary>
    public static double CoolingVibration(PlantOps ops, long tick)
    {
        var healthy = HealthyFanVibration(tick);
        var fault = Simulation.Vibration(UnitId.Clg, tick);
        var s = FanSpeedFraction(ops, tick);
        var cell3 = RunningFactor(ops.Device("clg.cell3"), tick, FanTau);
        return healthy + Math.Max(0, fault - healthy) * cell3 * s * s;
    }

    /// <summary>One cell's vibration, for the faceplate: the fault on cell 3, healthy elsewhere, nothing when stopped.</summary>
    public static double CellVibration(PlantOps ops, Device cell, long tick)
    {
        var running = RunningFactor(cell, tick, FanTau);
        var s = FanSpeedFraction(ops, tick);
        var v = cell.Id == "clg.cell3"
            ? HealthyFanVibration(tick) + Math.Max(0, Simulation.Vibration(UnitId.Clg, tick) - HealthyFanVibration(tick)) * s * s
            : HealthyFanVibration(tick + cell.Id.Length * 7);
        return v * running;
    }

    // ---------------------------------------------------------------- CND-07 condenser

    /// <summary>Cooling-water flow as a fraction of duty: either pump carries the full flow.</summary>
    public static double CoolingWaterFlow(PlantOps ops, long tick) =>
        Math.Min(1, ops.RunningSum(UnitId.Cnd, tick, VacuumTau));

    /// <summary>Absolute pressure in kPa: 9 at duty, toward 18 with no cooling water, 0.8 more per cell of cooling missing.</summary>
    public static double CondenserPressure(PlantOps ops, long tick, double drift) =>
        drift + 9 * (1 - CoolingWaterFlow(ops, tick)) + 0.8 * CoolingDeficit(ops, tick);

    /// <summary>MW lost to back-pressure: up to 40 MW as absolute pressure rises from 9 to 18 kPa.</summary>
    public static double VacuumLossMw(PlantOps ops, long tick) =>
        Math.Clamp((CondenserPressure(ops, tick, 9) - 9) / 9, 0, 1) * 40;

    // ---------------------------------------------------------------- TKA-02, SIL-06

    public static double FuelTransfer(PlantOps ops, long tick) =>
        Math.Min(1, ops.RunningSum(UnitId.Tka, tick, FuelTau));

    /// <summary>Silo fill and unload rates in % per tick.</summary>
    public const double SiloFillPerTick = 0.015, SiloUnloadPerTick = 0.1;

    public static double SiloLevel(PlantOps ops, long tick)
    {
        var m = ops.Silo;
        var rate = tick >= m.Tick && m.Unloading ? -SiloUnloadPerTick : SiloFillPerTick;
        return Math.Clamp(m.Level + rate * (tick - m.Tick), 20, 98);
    }

    // ---------------------------------------------------------------- valves

    /// <summary>A valve's actual opening at <paramref name="tick"/>, 0 to 1, travelling linearly after a move.</summary>
    public static double ValveOpening(PlantOps ops, Valve valve, long tick)
    {
        var target = valve.Open ? 1.0 : 0.0;
        if (!ops.ValveMoves.TryGetValue(valve.Id, out var moved) || tick >= moved + PlantOps.ValveTravelTicks) return target;
        if (tick < moved) return 1 - target;
        var progress = (tick - moved) / (double)PlantOps.ValveTravelTicks;
        return valve.Open ? progress : 1 - progress;
    }

    /// <summary>"Open", "Shut", "Opening…" or "Closing…".</summary>
    public static string ValveState(PlantOps ops, Valve valve, long tick)
    {
        var x = ValveOpening(ops, valve, tick);
        return x >= 1 ? "Open" : x <= 0 ? "Shut" : valve.Open ? "Opening…" : "Closing…";
    }

    /// <summary>Open valves counted by their actual opening, so output follows the travel.</summary>
    public static double OpenValves(PlantOps ops, long tick)
    {
        var open = 0.0;
        foreach (var v in ops.Valves) open += ValveOpening(ops, v, tick);
        return open;
    }
}
