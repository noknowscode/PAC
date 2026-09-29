using System.Runtime.CompilerServices;

namespace PacConsole.Core.Telemetry;

public interface ITelemetryService
{
    /// <summary>When true the stream stops advancing and the last snapshot stays current (control mode HOLD).</summary>
    bool IsPaused { get; set; }

    /// <summary>Yields the current snapshot immediately, then a new one per tick until cancelled.</summary>
    IAsyncEnumerable<PlantSnapshot> Stream(CancellationToken ct);
}

/// <summary>
/// Emits a <see cref="PlantSnapshot"/> every <see cref="Interval"/> using <see cref="Simulation"/>.
/// Readings are produced at the reference damper; the model applies the operator's damper setpoint.
/// </summary>
public sealed class SimulatedTelemetry(TimeProvider time) : ITelemetryService
{
    private long _tick = Simulation.StartTick;

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    public bool IsPaused { get; set; }

    public async IAsyncEnumerable<PlantSnapshot> Stream([EnumeratorCancellation] CancellationToken ct)
    {
        yield return Snapshot(_tick);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, time, ct).ConfigureAwait(false);
            if (IsPaused) continue;
            _tick++;
            yield return Snapshot(_tick);
        }
    }

    private PlantSnapshot Snapshot(long tick) => new(tick, time.GetLocalNow(), Simulation.Readings(tick));
}
