using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Core.Console;

/// <summary>One row of the alarm summary, or of the shelved alarms (<see cref="IsShelved"/>: Time then reads "until …").</summary>
[Uno.Extensions.Equality.ImplicitKeys(IsEnabled = false)]
public sealed record AlarmRowView(string Key, UnitId Unit, string Priority, string Time, string Title, string Detail, string State, string Tone, bool CanAck)
{
    /// <summary>On the shelf: the row offers Unshelve instead of Shelve.</summary>
    public bool IsShelved { get; init; }

    public string AutomationName => $"{Priority} priority, {State}, {Title}, {Detail}, {(IsShelved ? Time : "since " + Time)}";

    // Each row button names its action and its alarm (SPEC-OPERATIONS: every button names its action and target).
    public string SelectName => $"Select {UnitCatalog.Get(Unit).Code} for {Title}";
    public string AcknowledgeName => $"Acknowledge {Title}";
    public string ShelveName => $"Shelve {Title} for 1 hour";
    public string UnshelveName => $"Unshelve {Title}";
}

/// <summary>The response to the selected unit's alarm: cause, numbered steps, consequence.</summary>
[Uno.Extensions.Equality.ImplicitKeys(IsEnabled = false)]
public sealed record ResponseView(string Key, string Title, string Priority, string State, string Cause, string Steps, string Consequence, bool CanAck)
{
    public string AcknowledgeName => $"Acknowledge {Title}";
}

/// <summary>A selected command waiting for Confirm, or a refusal with its reason; <see cref="Command"/> in words.</summary>
public sealed record PendingView(string Text, string Reason, bool IsRefused, string Command)
{
    public string ConfirmName => $"Confirm: {Command}";
    public string CancelName => $"Cancel: {Command}";
}

/// <summary>A device on the selected unit's faceplate. Selecting it selects its start or stop.</summary>
[Uno.Extensions.Equality.ImplicitKeys(IsEnabled = false)]
public sealed record DeviceView(string Id, string Name, string State, string Detail, string Action, string Tone)
{
    public string AutomationName => $"{Name}, {State}{(Detail.Length > 0 ? ", " + Detail : "")}. Select to {Action.ToLowerInvariant()}";
}

/// <summary>One line of the operator log.</summary>
public sealed record LogRowView(string Time, string Kind, string Text, string Tone);

/// <summary>Operations (SPEC-OPERATIONS.md): alarms, response, select-before-operate, faceplates, log.</summary>
public sealed partial record ConsoleView
{
    public long Tick { get; init; }

    // Alarm summary: the first rows; "Show all" opens the rest below them.
    public const int AlarmRowsShown = 4;
    public string AlarmSummaryText { get; init; } = "";
    public ImmutableArray<AlarmRowView> AlarmRows { get; init; } = [];
    public ImmutableArray<AlarmRowView> ShownAlarmRows => [.. AlarmRows.Take(AlarmRowsShown)];
    public ImmutableArray<AlarmRowView> MoreAlarmRows => [.. AlarmRows.Skip(AlarmRowsShown)];
    public bool HasMoreAlarms => AlarmRows.Length > AlarmRowsShown;
    public string ShowAllAlarmsText => $"Show all {AlarmRows.Length}";

    /// <summary>Alarms on the shelf, soonest back first; each can be unshelved before its hour is up.</summary>
    public ImmutableArray<AlarmRowView> ShelvedRows { get; init; } = [];
    public bool HasShelvedRows => !ShelvedRows.IsEmpty;
    public bool CanAckAll { get; init; }

    // Response to the selected unit's alarm
    public ResponseView? Response { get; init; }

    // Select-before-operate
    public PendingView? Pending { get; init; }

    // Faceplate
    public ImmutableArray<DeviceView> Devices { get; init; } = [];
    public bool HasDevices => !Devices.IsEmpty;
    public bool ShowFanSpeed => Selected == UnitId.Clg;
    public bool ShowLoad => Selected == UnitId.Trb;
    public bool ShowReactive => Selected == UnitId.Gen;
    public bool ShowDamper => Selected == UnitId.Stk;
    public string FanSpeedText { get; init; } = "";
    public string LoadText { get; init; } = "";
    public string LimitText { get; init; } = "";
    public string ReactiveText { get; init; } = "";
    public string DamperText { get; init; } = "";

    // What the mode does to the setpoints, in words beside the controls it changes.
    public string ModeHint => Mode switch
    {
        ControlMode.Manual => "Manual: the ramps hold where they are, and a setpoint you apply takes effect at once.",
        ControlMode.Hold => "Hold: the console keeps the last snapshot until you leave Hold.",
        _ => "Auto: setpoints ramp to the value you apply.",
    };
    public string FanSpeedHint => Hint(SetpointRange.FanSpeed, "%", "ramps 1 % every 2 s");
    public string LoadHint => Hint(SetpointRange.Load, "MW", "ramps 5 MW a minute");
    public string ReactiveHint => Hint(SetpointRange.Reactive, "MVAr", "ramps 2 MVAr every 2 s");

    private string Hint(SetpointRange range, string unit, string ramp) =>
        $"{range.Min.ToString("0", Inv)} to {range.Max.ToString("0", Inv)} {unit}; {(IsManual ? "applies at once in Manual" : ramp)}.";

    // Operator log: the latest lines, newest first (the model keeps 200).
    public const int LogRowsShown = 8;
    public ImmutableArray<LogRowView> LogRows { get; init; } = [];
    public bool LogIsEmpty => LogRows.IsEmpty;

    /// <summary>Adds the operations fields: alarm summary, response, pending command, faceplate and log.</summary>
    private static ConsoleView WithOps(ConsoleView view, PlantSnapshot snapshot, Setpoints setpoints, PlantOps ops, AlarmBook book)
    {
        var tick = snapshot.Tick;
        string TimeOf(long t) => (snapshot.At - TimeSpan.FromSeconds((tick - t) * 2)).ToLocalTime().ToString("HH:mm:ss", Inv);
        string Value(AlarmEntry e, double v) => $"{v.ToString(e.Format, Inv)}";

        static string PriorityWord(AlarmPriority p) => p.ToString();
        static string StateWord(AlarmState s) => s switch
        {
            AlarmState.Unacknowledged => "Unacknowledged",
            AlarmState.Acknowledged => "Acknowledged",
            AlarmState.ReturnedUnacknowledged => "Returned · unacknowledged",
            _ => "Shelved",
        };

        var listed = book.Entries.Where(e => e.State != AlarmState.Shelved).ToList();
        var rows = listed.Select(e => new AlarmRowView(
            e.Key,
            e.Unit,
            PriorityWord(e.Priority),
            e.OnsetBeforeWindow ? $"before {TimeOf(e.OnsetTick)}" : TimeOf(e.OnsetTick),
            e.Title,
            e.IsActive ? $"{Value(e, e.Value)} / {Value(e, e.Limit)} {e.UnitOfMeasure}".TrimEnd() : "Returned to normal",
            StateWord(e.State),
            e.State switch { AlarmState.Unacknowledged => "unacked", AlarmState.ReturnedUnacknowledged => "returned", _ => "acked" },
            e.NeedsAck)).ToImmutableArray();

        // On the shelf for its hour, whether it still stands or has returned to normal since.
        var shelved = ops.Shelves.Where(s => s.Value > tick).OrderBy(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).Select(s =>
        {
            var standing = book.Entries.FirstOrDefault(e => e.Key == s.Key);
            return new AlarmRowView(s.Key, EquipmentCatalog.UnitOf(s.Key), PriorityWord(EquipmentCatalog.Find(s.Key)!.Alarm!.Priority),
                $"until {TimeOf(s.Value)}", AlarmBook.Title(s.Key),
                standing is { } e ? $"{Value(e, e.Value)} / {Value(e, e.Limit)} {e.UnitOfMeasure}".TrimEnd() : "Returned to normal",
                StateWord(AlarmState.Shelved), "acked", CanAck: false) { IsShelved = true };
        }).ToImmutableArray();

        var summary = new List<string>();
        if (book.ActiveCount > 0) summary.Add($"{book.ActiveCount} active");
        if (book.UnacknowledgedCount > 0) summary.Add($"{book.UnacknowledgedCount} unacknowledged");
        if (shelved.Length > 0) summary.Add($"{shelved.Length} shelved");

        var mine = book.Entries.FirstOrDefault(e => e.Unit == view.Selected && e.State != AlarmState.Shelved);
        ResponseView? response = null;
        if (mine is not null)
        {
            var g = EquipmentCatalog.Find(mine.Key)!.Alarm!.Guidance;
            response = new ResponseView(mine.Key, mine.Title, PriorityWord(mine.Priority), StateWord(mine.State), g.Cause,
                string.Join("\n", g.Steps.Select((s, i) => $"{i + 1}. {s}")), g.Consequence, mine.NeedsAck);
        }

        PendingView? pending = ops.Pending is { } p && !p.IsExpired(tick)
            ? p.Refusal is { } reason
                ? new PendingView($"Cannot {p.Command.Describe()}", reason, true, p.Command.Describe())
                : new PendingView($"Confirm: {p.Command.Describe()}?", "Select-before-operate: confirm within 10 s, or press Esc to cancel.", false, p.Command.Describe())
            : null;

        var devices = ops.DevicesOf(view.Selected).Select(d =>
        {
            var state = d.StateAt(tick);
            var detail = d.Unit == UnitId.Clg
                ? $"{PlantEffects.CellVibration(ops, d, tick).ToString("0.0", Inv)} mm/s"
                : d.Id == "sil.unload" ? $"silo {PlantEffects.SiloLevel(ops, tick).ToString("0", Inv)} %" : "";
            return new DeviceView(d.Id, d.Name, state, detail, d.Running ? "Stop" : "Start",
                state.EndsWith('…') ? "moving" : d.Running ? "running" : "stopped");
        }).ToImmutableArray();

        // A step (Manual) reaches its target at the next scan: it shows the target, never a minute of ramp.
        static string RampText(Ramp r, long t, string unit, string format) =>
            r.IsMoving(t) && !r.IsStep ? $"{r.ValueAt(t).ToString(format, Inv)} → {r.To.ToString(format, Inv)} {unit} · {Math.Max(1, r.MinutesLeft(t))} min"
            : r.IsHeld(t) ? $"{r.ValueAt(t).ToString(format, Inv)} {unit} · held, target {r.To.ToString(format, Inv)}"
            : $"{r.To.ToString(format, Inv)} {unit}";

        // Newest first, by tick and then by the order the lines were written: lines often share a tick (Acknowledge
        // all writes one per alarm, and under HOLD the tick stands still), and the latest of them must lead. The
        // operator acts on a snapshot after its alarms came in, and a command takes effect from the next scan, so a
        // tick's own lines follow its alarm transitions and an action always precedes what it caused. Every line reads
        // the plant's clock, the time of its tick's snapshot, so the times agree with that order.
        var log = book.Transitions.Select(t => (t.Tick, Row: new LogRowView(TimeOf(t.Tick), t.Raised ? "Alarm" : "Normal", t.Text, t.Raised ? "alarm" : "normal")))
            .Concat(ops.Log.Select(e => (e.Tick, Row: new LogRowView(TimeOf(e.Tick), KindWord(e.Kind), WithUnit(e.Unit, e.Text), e.Kind.ToString().ToLowerInvariant()))))
            .Select((x, written) => (x.Tick, x.Row, Written: written))
            .OrderByDescending(x => x.Tick)
            .ThenByDescending(x => x.Written)
            .Take(LogRowsShown)
            .Select(x => x.Row)
            .ToImmutableArray();

        return view with
        {
            Tick = tick,
            AlarmSummaryText = summary.Count == 0 ? "No standing alarms" : string.Join(" · ", summary),
            AlarmRows = rows,
            ShelvedRows = shelved,
            CanAckAll = rows.Any(r => r.CanAck),
            Response = response,
            Pending = pending,
            Devices = devices,
            FanSpeedText = RampText(ops.FanSpeed, tick, "%", "0"),
            LoadText = RampText(ops.Load, tick, "MW", "0"),
            ReactiveText = RampText(ops.Reactive, tick, "MVAr", "0"),
            LimitText = $"{setpoints.TurbineLimitBar.ToString("0", Inv)} bar",
            DamperText = $"{setpoints.DamperPct.ToString("0", Inv)} %",
            LogRows = log,
        };
    }

    private static string KindWord(LogKind kind) => kind switch
    {
        LogKind.Acknowledge => "Ack",
        LogKind.Shelve => "Shelved",
        LogKind.Unshelve => "Unshelved",
        _ => kind.ToString(),
    };

    /// <summary>Leads with the unit's code unless the text already names it ("Stop Cell 3 on CLG-05").</summary>
    private static string WithUnit(UnitId? unit, string text) =>
        unit is { } u && !text.Contains(UnitCatalog.Get(u).Code, StringComparison.Ordinal) ? $"{UnitCatalog.Get(u).Code} · {text}" : text;
}
