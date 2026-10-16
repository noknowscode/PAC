using System.Globalization;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Operations;

/// <summary>A piece of plant the operator starts and stops: a fan cell, a pump, the silo's unloading.</summary>
/// <param name="ChangedTick">When it was last commanded; 0 means it has always been in this state.</param>
public sealed record Device(string Id, UnitId Unit, string Name, bool Running, long ChangedTick = 0)
{
    /// <summary>Ticks a start takes before the device reports Running (a stop takes <see cref="StopTicks"/>).</summary>
    public const int StartTicks = 3;
    public const int StopTicks = 2;

    /// <summary>"Running", "Stopped", "Starting…" or "Stopping…" at <paramref name="tick"/>.</summary>
    public string StateAt(long tick)
    {
        var since = tick - ChangedTick;
        if (ChangedTick > 0 && since < (Running ? StartTicks : StopTicks)) return Running ? "Starting…" : "Stopping…";
        return Running ? "Running" : "Stopped";
    }
}

/// <summary>A setpoint moving from <see cref="From"/> to <see cref="To"/> at a fixed rate from <see cref="Tick"/>.</summary>
public sealed record Ramp(double From, double To, long Tick, double PerTick)
{
    public static Ramp Hold(double value) => new(value, value, 0, 1);

    public double ValueAt(long tick)
    {
        if (tick <= Tick) return From;
        var step = PerTick * (tick - Tick);
        return To >= From ? Math.Min(To, From + step) : Math.Max(To, From - step);
    }

    public bool IsMoving(long tick) => ValueAt(tick) != To;

    /// <summary>Whole minutes left until the ramp reaches its target (one tick is 2 s).</summary>
    public int MinutesLeft(long tick) => (int)Math.Ceiling(Math.Abs(To - ValueAt(tick)) / PerTick * 2 / 60);
}

/// <summary>The ash silo's level at a known tick, and whether it was unloading from then on.</summary>
public sealed record SiloMark(long Tick, double Level, bool Unloading);

public enum LogKind { Alarm, Action, Acknowledge, Shelve, Note, Refused }

/// <summary>One line of the operator log.</summary>
public sealed record LogEntry(long Tick, DateTimeOffset At, LogKind Kind, UnitId? Unit, string Text, string By);

/// <summary>What an operator can select for select-before-operate.</summary>
public abstract record OpsCommand(UnitId Unit)
{
    /// <summary>The command in words, for the confirm bar and the log: "stop Cell 3 on CLG-05".</summary>
    public abstract string Describe();
}

public sealed record StartDevice(UnitId Unit, string DeviceId, string Name) : OpsCommand(Unit)
{
    public override string Describe() => $"start {Name} on {UnitCatalog.Get(Unit).Code}";
}

public sealed record StopDevice(UnitId Unit, string DeviceId, string Name) : OpsCommand(Unit)
{
    public override string Describe() => $"stop {Name} on {UnitCatalog.Get(Unit).Code}";
}

public sealed record MoveValve(UnitId Unit, int ValveId, bool Open) : OpsCommand(Unit)
{
    public override string Describe() => $"{(Open ? "open" : "close")} valve V-{ValveId} on {UnitCatalog.Get(Unit).Code}";
}

/// <summary>A selected command awaiting Confirm, or a refusal to show with its reason.</summary>
public sealed record Pending(OpsCommand Command, long Tick, string? Refusal)
{
    /// <summary>A selection lapses after five ticks (10 s) unconfirmed.</summary>
    public const int ExpiresAfterTicks = 5;

    public bool IsExpired(long tick) => tick - Tick > ExpiresAfterTicks;
}

/// <summary>
/// The operator side of the plant: what is running, where the ramps are heading, the valves'
/// commanded positions, which alarms were acknowledged or shelved, the pending selection and the
/// log. Immutable; every action returns a new value, so the console's rules stay pure and tested.
/// </summary>
public sealed record PlantOps(
    ImmutableDictionary<string, Device> Devices,
    ImmutableArray<Valve> Valves,
    ImmutableDictionary<int, long> ValveMoves,
    Ramp FanSpeed,
    Ramp Load,
    Ramp Reactive,
    SiloMark Silo,
    ImmutableDictionary<string, long> Acks,
    ImmutableDictionary<string, long> Shelves,
    ImmutableList<LogEntry> Log,
    Pending? Pending)
{
    public const string OperatorName = "Operator 04";

    /// <summary>Ticks a valve takes to travel between shut and open.</summary>
    public const int ValveTravelTicks = 5;

    /// <summary>Ramp rates per tick (one tick is 2 s): load 5 MW/min, fan speed 1 %, reactive power 2 MVAr.</summary>
    public const double LoadPerTick = 5.0 / 30, FanPerTick = 1, ReactivePerTick = 2;

    /// <summary>Shelving lasts an hour.</summary>
    public const int ShelveTicks = 1800;

    public const int MinRunningCells = 4;

    public static readonly PlantOps Initial = new(
        Devices: new Device[]
        {
            new("clg.cell1", UnitId.Clg, "Cell 1", true),
            new("clg.cell2", UnitId.Clg, "Cell 2", true),
            new("clg.cell3", UnitId.Clg, "Cell 3", true),
            new("clg.cell4", UnitId.Clg, "Cell 4", true),
            new("clg.cell5", UnitId.Clg, "Cell 5", true),
            new("clg.cell6", UnitId.Clg, "Cell 6 (standby)", false),
            new("cnd.cwA", UnitId.Cnd, "CW pump A", true),
            new("cnd.cwB", UnitId.Cnd, "CW pump B", false),
            new("tka.p1", UnitId.Tka, "Transfer pump P1", true),
            new("tka.p2", UnitId.Tka, "Transfer pump P2", false),
            new("sil.unload", UnitId.Sil, "Unloading", false),
        }.ToImmutableDictionary(d => d.Id),
        Valves: Valve.Defaults,
        ValveMoves: ImmutableDictionary<int, long>.Empty,
        FanSpeed: Ramp.Hold(100),
        Load: Ramp.Hold(620),
        Reactive: Ramp.Hold(120),
        Silo: new SiloMark(Simulation.StartTick, 78, false),
        Acks: ImmutableDictionary<string, long>.Empty,
        Shelves: ImmutableDictionary<string, long>.Empty,
        Log: [],
        Pending: null);

    public Device Device(string id) => Devices[id];

    /// <summary>Each unit's device ids in faceplate order (fixed, so effects never sort on the hot path).</summary>
    private static readonly Dictionary<UnitId, string[]> Groups = new()
    {
        [UnitId.Clg] = ["clg.cell1", "clg.cell2", "clg.cell3", "clg.cell4", "clg.cell5", "clg.cell6"],
        [UnitId.Cnd] = ["cnd.cwA", "cnd.cwB"],
        [UnitId.Tka] = ["tka.p1", "tka.p2"],
        [UnitId.Sil] = ["sil.unload"],
    };

    public IEnumerable<Device> DevicesOf(UnitId unit) =>
        Groups.TryGetValue(unit, out var ids) ? ids.Select(id => Devices[id]) : [];

    /// <summary>The sum of a unit's devices' running factors, without allocating.</summary>
    public double RunningSum(UnitId unit, long tick, double tau)
    {
        if (!Groups.TryGetValue(unit, out var ids)) return 0;
        var sum = 0.0;
        foreach (var id in ids) sum += PlantEffects.RunningFactor(Devices[id], tick, tau);
        return sum;
    }

    // ---------------------------------------------------------------- select-before-operate

    /// <summary>Selects a command: it waits for Confirm, or is refused at once with the reason.</summary>
    public PlantOps Select(OpsCommand command, long tick) => this with { Pending = new Pending(command, tick, Permissive(command)) };

    public PlantOps Cancel() => this with { Pending = null };

    /// <summary>Carries out the pending command if it is still valid; a lapsed or refused one is dropped (and a refusal logged).</summary>
    public PlantOps Confirm(long tick, DateTimeOffset now)
    {
        if (Pending is not { } p || p.IsExpired(tick)) return Cancel();
        if (p.Refusal is { } reason) return Cancel().Logged(tick, now, LogKind.Refused, p.Command.Unit, $"Refused: {p.Command.Describe()}. {reason}");
        // A permissive can change between select and confirm (another cell stopped meanwhile).
        if (Permissive(p.Command) is { } late) return Cancel().Logged(tick, now, LogKind.Refused, p.Command.Unit, $"Refused: {p.Command.Describe()}. {late}");
        var done = p.Command switch
        {
            StartDevice s => SetRunning(s.DeviceId, true, tick),
            StopDevice s => SetRunning(s.DeviceId, false, tick),
            MoveValve v => this with
            {
                Valves = [.. Valves.Select(x => x.Id == v.ValveId ? x with { Open = v.Open } : x)],
                ValveMoves = ValveMoves.SetItem(v.ValveId, tick),
            },
            _ => this,
        };
        return done.Cancel().Logged(tick, now, LogKind.Action, p.Command.Unit, Capitalise(p.Command.Describe()));
    }

    /// <summary>Why a command may not run now, or null. Interlocks keep the plant inside its envelope.</summary>
    public string? Permissive(OpsCommand command) => command switch
    {
        StopDevice { DeviceId: var id } when id.StartsWith("clg.cell", StringComparison.Ordinal)
            && DevicesOf(UnitId.Clg).Count(d => d.Running) <= MinRunningCells
            => $"Keep at least {MinRunningCells} cells running: start the standby cell first.",
        StopDevice { DeviceId: var id } when id.StartsWith("cnd.cw", StringComparison.Ordinal)
            && DevicesOf(UnitId.Cnd).Count(d => d.Running) <= 1
            => "Stopping the last cooling-water pump loses condenser vacuum: start the other pump first.",
        StopDevice { DeviceId: var id } when id.StartsWith("tka.p", StringComparison.Ordinal)
            && DevicesOf(UnitId.Tka).Count(d => d.Running) <= 1
            => "The burners need a transfer pump: start the other pump first.",
        StartDevice { DeviceId: var id } when Device(id).Running => "Already running.",
        StopDevice { DeviceId: var id } when !Device(id).Running => "Already stopped.",
        MoveValve { ValveId: >= 101 and <= 103, Open: false } when Valves.Count(v => v.Id is >= 101 and <= 103 && v.Open) <= 1
            => "Keep one turbine admission valve open while generating.",
        _ => null,
    };

    private PlantOps SetRunning(string id, bool running, long tick)
    {
        var next = this with { Devices = Devices.SetItem(id, Device(id) with { Running = running, ChangedTick = tick }) };
        // The silo keeps its level continuous: mark where it was when unloading starts or stops.
        return id == "sil.unload" ? next with { Silo = new SiloMark(tick, PlantEffects.SiloLevel(this, tick), running) } : next;
    }

    // ---------------------------------------------------------------- setpoints

    public PlantOps SetLoad(double mw, long tick, DateTimeOffset now) =>
        (this with { Load = new Ramp(Load.ValueAt(tick), mw, tick, LoadPerTick) })
            .Logged(tick, now, LogKind.Action, UnitId.Trb, $"Load setpoint {Load.ValueAt(tick).ToString("0", Inv)} → {mw.ToString("0", Inv)} MW (5 MW/min)");

    public PlantOps SetFanSpeed(double pct, long tick, DateTimeOffset now) =>
        (this with { FanSpeed = new Ramp(FanSpeed.ValueAt(tick), pct, tick, FanPerTick) })
            .Logged(tick, now, LogKind.Action, UnitId.Clg, $"Fan speed setpoint {FanSpeed.ValueAt(tick).ToString("0", Inv)} → {pct.ToString("0", Inv)} %");

    public PlantOps SetReactive(double mvar, long tick, DateTimeOffset now) =>
        (this with { Reactive = new Ramp(Reactive.ValueAt(tick), mvar, tick, ReactivePerTick) })
            .Logged(tick, now, LogKind.Action, UnitId.Gen, $"Reactive power setpoint {Reactive.ValueAt(tick).ToString("0", Inv)} → {mvar.ToString("0", Inv)} MVAr");

    /// <summary>Records a setpoint that applies at once (the turbine limit, the damper).</summary>
    public PlantOps Noted(UnitId unit, string text, long tick, DateTimeOffset now) => Logged(tick, now, LogKind.Action, unit, text);

    // ---------------------------------------------------------------- alarms and notes

    public PlantOps Acknowledge(string key, string description, UnitId unit, long tick, DateTimeOffset now) =>
        (this with { Acks = Acks.SetItem(key, tick) }).Logged(tick, now, LogKind.Acknowledge, unit, $"Acknowledged: {description}");

    public PlantOps Shelve(string key, string description, UnitId unit, long tick, DateTimeOffset now) =>
        (this with { Shelves = Shelves.SetItem(key, tick + ShelveTicks), Acks = Acks.SetItem(key, tick) })
            .Logged(tick, now, LogKind.Shelve, unit, $"Shelved for 1 h: {description}");

    public PlantOps AddNote(string text, long tick, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(text) ? this : Logged(tick, now, LogKind.Note, null, text.Trim());

    public bool IsShelved(string key, long tick) => Shelves.TryGetValue(key, out var until) && until > tick;

    /// <summary>Adds a log line; the log keeps the latest 200.</summary>
    public PlantOps Logged(long tick, DateTimeOffset now, LogKind kind, UnitId? unit, string text)
    {
        var log = Log.Add(new LogEntry(tick, now, kind, unit, text, OperatorName));
        return this with { Log = log.Count > 200 ? log.RemoveRange(0, log.Count - 200) : log };
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
