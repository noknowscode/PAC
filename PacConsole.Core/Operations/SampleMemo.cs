using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Operations;

/// <summary>One tick of the simulated plant under given setpoints and operator actions.</summary>
public sealed class PlantSample(ImmutableArray<UnitReading> readings, double netMw, Setpoints setpoints)
{
    public ImmutableArray<UnitReading> Readings { get; } = readings;
    public double NetMw { get; } = netMw;

    /// <summary>The setpoints in force at this tick (<see cref="PlantOps.SetpointsAt"/>).</summary>
    public Setpoints Setpoints { get; } = setpoints;

    internal ImmutableArray<Alarm>? Alarms { get; set; }
}

/// <summary>
/// Remembers simulated ticks while the setpoints and the operator's actions stay the same. The alarm
/// replay and the trends sample fixed offsets back from the current tick, so a new snapshot needs only
/// the ticks it has not seen; an action or a setpoint change starts afresh (measured: the view's
/// compose fell from about 40 ms to under 5 ms).
/// </summary>
public static class SampleMemo
{
    private static readonly object Gate = new();
    private static readonly Dictionary<long, PlantSample> Samples = [];
    private static PlantOps? _ops;
    private static Setpoints? _setpoints;

    /// <summary>Bounds the memory: about two 8-hour trend windows of sub-samples.</summary>
    private const int Capacity = 60_000;

    public static PlantSample At(long tick, Setpoints setpoints, PlantOps ops)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(ops, _ops) || setpoints != _setpoints || Samples.Count > Capacity)
            {
                Samples.Clear();
                _ops = ops;
                _setpoints = setpoints;
            }
            if (Samples.TryGetValue(tick, out var hit)) return hit;
            var applied = ops.SetpointsAt(tick, setpoints);
            var sample = new PlantSample(
                Simulation.Readings(tick, applied.DamperPct, ops),
                Simulation.OutputMw(tick, PlantEffects.OpenValves(ops, tick), ops),
                applied);
            Samples[tick] = sample;
            return sample;
        }
    }

    /// <summary>The alarms standing at a tick, remembered with its sample.</summary>
    public static ImmutableArray<Alarm> AlarmsAt(long tick, Setpoints setpoints, PlantOps ops)
    {
        var sample = At(tick, setpoints, ops);
        lock (Gate)
        {
            return sample.Alarms ??= AlarmRules.Evaluate(tick, sample.Readings, sample.NetMw, sample.Setpoints, ops);
        }
    }
}
