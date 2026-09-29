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

    /// <summary>Ticks per simulated hour for the hourly-generation bars.</summary>
    public const int TicksPerHour = 30;

    public const int OutputHistoryLength = 48;
    public const int HourlyLength = 24;
    public const int VibrationHistoryLength = 30;

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

    public static ImmutableArray<UnitReading> Readings(long tick, double damperPct = ReferenceDamperPct)
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
                Vib: Vibration(u.Id, tick)));
        }
        return builder.MoveToImmutable();
    }

    public static double Vibration(UnitId id, long tick)
    {
        var i = (int)id;
        return UnitCatalog.Get(id).BaseVib + 0.16 * Math.Sin(tick * 0.9 + i * 1.7) + (Noise(tick, i + 3) - 0.5) * 0.14;
    }

    public static ImmutableArray<double> VibrationHistory(UnitId id, long tick) =>
        [.. Enumerable.Range(0, VibrationHistoryLength).Select(k => Vibration(id, tick - (VibrationHistoryLength - 1) + k))];

    /// <summary>Net output in MW. Each open valve beyond the reference seven adds 6 MW.</summary>
    public static double OutputMw(long tick, int openValves = ReferenceOpenValves) =>
        600
        + 22 * Math.Sin(tick * 0.21)
        + 12 * Math.Sin(tick * 0.057 + 1)
        + (Noise(tick, 1) - 0.5) * 14
        + (openValves - ReferenceOpenValves) * 6;

    public static ImmutableArray<double> OutputHistory(long tick, int openValves = ReferenceOpenValves) =>
        [.. Enumerable.Range(0, OutputHistoryLength).Select(k => OutputMw(tick - (OutputHistoryLength - 1) + k, openValves))];

    /// <summary>The last 24 simulated hours of generation in MWh; the last element is the current hour.</summary>
    public static ImmutableArray<double> HourlyGeneration(long tick)
    {
        var hour = (long)Math.Floor(tick / (double)TicksPerHour);
        return [.. Enumerable.Range(0, HourlyLength).Select(k =>
        {
            var hh = hour - (HourlyLength - 1) + k;
            return 560 + 70 * Math.Sin((hh - 8) / 24.0 * Math.Tau) + (Noise(hh, 7) - 0.5) * 30;
        })];
    }
}
