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

    /// <summary>
    /// Fault switch for exercising the console's error state: the stream throws
    /// <see cref="TelemetryFaultException"/> instead of its next snapshot once this many have been
    /// sent, then the switch clears, so a new stream runs normally.
    /// </summary>
    public int? FaultAfter { get; set; }

    public async IAsyncEnumerable<PlantSnapshot> Stream([EnumeratorCancellation] CancellationToken ct)
    {
        var sent = 0;
        yield return Snapshot(_tick);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(Interval, time, ct).ConfigureAwait(false);
            if (IsPaused) continue;
            if (++sent >= FaultAfter)
            {
                FaultAfter = null;
                throw new TelemetryFaultException("Simulated telemetry fault: the stream from plant 02 dropped.");
            }
            _tick++;
            yield return Snapshot(_tick);
        }
    }

    private PlantSnapshot Snapshot(long tick) => new(tick, time.GetLocalNow(), Simulation.Readings(tick));
}

/// <summary>The telemetry stream failed; the console shows it as a stopped stream with Retry.</summary>
public sealed class TelemetryFaultException(string message) : Exception(message);
