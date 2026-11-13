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
/// Emits a <see cref="PlantSnapshot"/> every <see cref="Interval"/> using <see cref="Simulation"/>, from
/// <paramref name="startTick"/>: the prototype's first tick on a first launch, or where a saved operator record says
/// the plant has got to since. Readings are produced at the reference damper; the model applies the operator's damper.
/// </summary>
public sealed class SimulatedTelemetry(TimeProvider time, long startTick = Simulation.StartTick) : ITelemetryService
{
    /// <summary>One snapshot every 2 s, the plant's tick.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    private long _tick = startTick;

    public TimeSpan Interval { get; init; } = TickInterval;

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
public sealed class TelemetryFaultException(string message) : Exception(message)
{
    /// <summary>
    /// What the console shows once a stream has stopped, until Retry: its fault; a fault saying it ended, when it
    /// ended after sending data (the last values are no longer current); or null when it never sent a snapshot,
    /// which is the "no telemetry" state rather than an error.
    /// </summary>
    public static Exception? ForStop(Exception? fault, bool sentData) =>
        fault ?? (sentData ? new TelemetryFaultException("The telemetry stream from plant 02 ended.") : null);
}
