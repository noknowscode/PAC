using PacConsole.Core.Operations;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Telemetry;

/// <summary>
/// Deterministic plant simulation: the prototype's sine-and-hash formulas, ported exactly so the
/// app and the HTML prototype show the same numbers at the same tick. Every value is a pure function
/// of the tick and the operator's inputs, which is what makes screenshots and tests repeatable.
/// </summary>
public static class Simulation
{
    /// <summary>The prototype starts at tick 120.</summary>
    public const long StartTick = 120;

    /// <summary>Damper position the base flows were measured at.</summary>
    public const double ReferenceDamperPct = 65;

    /// <summary>Open valves the base output was measured with.</summary>
    public const int ReferenceOpenValves = 7;

    /// <summary>Hash noise in [0, 1): the classic GLSL one-liner, so it matches the prototype bit for bit in practice.</summary>
    public static double Noise(double i, double seed)
    {
        var h = Math.Sin(i * 12.9898 + seed * 78.233) * 43758.5453;
        return h - Math.Floor(h);
    }

    /// <summary>Every unit's base readings; <paramref name="ops"/> carries the operator's actions (the cooling bank's cells and fan speed).</summary>
    public static ImmutableArray<UnitReading> Readings(long tick, double damperPct = ReferenceDamperPct, PlantOps? ops = null)
    {
        var builder = ImmutableArray.CreateBuilder<UnitReading>(UnitCatalog.All.Length);
        for (var i = 0; i < UnitCatalog.All.Length; i++)
        {
            var u = UnitCatalog.All[i];
            double W(double b, double seed, double amp) => b * (1 + amp * Math.Sin(tick * 0.5 + seed));
            var flowBase = u.Id == UnitId.Stk ? u.BaseFlow * damperPct / ReferenceDamperPct : u.BaseFlow;
            builder.Add(new UnitReading(
                u.Id,
                Temp: W(u.BaseTemp, i, 0.006),
                Press: W(u.BasePress, i + 1, 0.004),
                Flow: W(flowBase, i + 2, 0.012),
                Load: W(u.BaseLoad, i + 3, 0.01),
                Eff: W(u.BaseEff, i + 4, 0.002),
                Vib: u.Id == UnitId.Clg ? PlantEffects.CoolingVibration(ops ?? PlantOps.Initial, tick) : Vibration(u.Id, tick)));
        }
        return builder.MoveToImmutable();
    }

    public static double Vibration(UnitId id, long tick)
    {
        var i = (int)id;
        var v = UnitCatalog.Get(id).BaseVib + 0.16 * Math.Sin(tick * 0.9 + i * 1.7) + (Noise(tick, i + 3) - 0.5) * 0.14;
        // Cell 3 of the cooling bank drifts in and out of alarm with a ten-minute period (tick = 2 s),
        // centred on 4.5 mm/s with a ±0.7 swing so it also clears below the 4.0 mm/s deadband, and in
        // alarm at the start tick, so trends over 15 min to 8 h carry real alarm onsets.
        if (id == UnitId.Clg) v += 0.7 * Math.Sin(tick * Math.Tau / 300 - 1.0) - 0.1;
        return v;
    }

    /// <summary>
    /// Net output in MW: the turbine's load setpoint less 20 MW of auxiliaries, a slow drift (about
    /// 52 min, ±18), a swell (about 5 min, ±8), a small ripple and noise, so the hour's trend reads as
    /// load following rather than a sine. Each open valve beyond the reference seven adds 6 MW; lost
    /// condenser vacuum takes up to 40 MW.
    /// </summary>
    public static double OutputMw(long tick, double openValves = ReferenceOpenValves, PlantOps? ops = null)
    {
        var o = ops ?? PlantOps.Initial;
        return o.Load.ValueAt(tick) - 20
            + 18 * Math.Sin(tick * 0.004 + 0.6)
            + 8 * Math.Sin(tick * 0.021 + 1)
            + 3 * Math.Sin(tick * 0.21)
            + (Noise(tick, 1) - 0.5) * 4
            + (openValves - ReferenceOpenValves) * 6
            - PlantEffects.VacuumLossMw(o, tick);
    }
}
