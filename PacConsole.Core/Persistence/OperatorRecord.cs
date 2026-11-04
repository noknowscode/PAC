using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Persistence;

/// <summary>
/// What the console keeps across a relaunch (SPEC-OPERATIONS: actions, acknowledgements and the log are durable): the
/// operator side of the plant, its mode, and the plant tick and wall time it was saved at, so the simulated plant
/// carries on from where it was rather than from its first tick. A pending select-before-operate is never kept: it
/// lapses in 10 s anyway, and a relaunch must not leave a command one press from running.
/// </summary>
public sealed record OperatorRecord(long Tick, DateTimeOffset SavedAt, ControlMode Mode, PlantOps Ops)
{
    /// <summary>
    /// The tick to resume the simulated plant at: the saved tick plus one per telemetry interval since it was saved, so
    /// ramps, shelves and device starts read as time having passed. A clock set back never runs the plant backwards.
    /// </summary>
    public long ResumeTick(DateTimeOffset now, TimeSpan interval)
    {
        var elapsed = now - SavedAt;
        return elapsed <= TimeSpan.Zero ? Tick : Tick + (long)(elapsed.Ticks / interval.Ticks);
    }

    /// <summary>The record for one moment of the console, with the pending command dropped.</summary>
    public static OperatorRecord Of(long tick, DateTimeOffset now, ControlMode mode, PlantOps ops) =>
        new(tick, now, mode, ops with { Pending = null });
}
