using PacConsole.Core.Console;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Presentation;

/// <summary>
/// Domain state for the console. Telemetry arrives as a stream; the operator's inputs are
/// states; <see cref="View"/> combines them through one pure function in Core.
/// Camera, orbit and hover state live in PlantViewport, not here: they change every frame
/// and are not domain data.
/// </summary>
public partial record ConsoleModel
{
    private readonly ITelemetryService _telemetry;
    private readonly TimeProvider _time;

    public ConsoleModel(ITelemetryService telemetry, TimeProvider time)
    {
        _telemetry = telemetry;
        _time = time;
    }

    public IFeed<PlantSnapshot> Snapshot => Feed.AsyncEnumerable(_telemetry.Stream);

    /// <summary>1 s wall clock for the header and the held-data age.</summary>
    public IFeed<DateTimeOffset> Clock => Feed.AsyncEnumerable(Ticks);

    // Applied setpoints. Written only by the Apply commands (SPEC-OPERATIONS.md: typed, then applied).
    public IState<double> TargetMw => State.Value(this, () => Setpoints.Default.TargetMw);
    public IState<double> TurbineLimitBar => State.Value(this, () => Setpoints.Default.TurbineLimitBar);
    public IState<double> DamperPct => State.Value(this, () => Setpoints.Default.DamperPct);

    // Drafts: each written only by its NumberBox (two-way), read by its Apply command as a feed parameter.
    public IState<double> LoadDraft => State.Value(this, () => Setpoints.Default.TargetMw);
    public IState<double> LimitDraft => State.Value(this, () => Setpoints.Default.TurbineLimitBar);
    public IState<double> DamperDraft => State.Value(this, () => Setpoints.Default.DamperPct);
    public IState<double> FanSpeedDraft => State.Value(this, () => 100.0);
    public IState<double> ReactiveDraft => State.Value(this, () => 120.0);
    public IState<string> NoteDraft => State.Value(this, () => "");

    /// <summary>The operator side of the plant: devices, ramps, valves, acknowledgements, shelves, the log and the pending command.</summary>
    public IState<PlantOps> Ops => State.Value(this, () => PlantOps.Initial);

    public IState<ControlMode> Mode => State.Value(this, () => ControlMode.Auto);

    /// <summary>
    /// Opens on the cooling bank, the unit in alert at start. Pinned rather than left to follow the
    /// first alert: the cooling bank drifts in and out of alarm, and the inspector must not change
    /// equipment on its own. Wrapped in a record because MVUX treats null as "no value".
    /// </summary>
    public IState<UnitSelection> Selection => State.Value(this, () => new UnitSelection(UnitId.Clg));


    // Console v3 view inputs (SPEC-CONSOLE.md). Each has one writer: its command, or the cursor slider.
    public IState<PlantSystem> System => State.Value(this, () => PlantSystem.All);
    public IState<TrendRange> Range => State.Value(this, () => TrendRange.Minutes15);

    /// <summary>Reading keys plotted under net generation, for one item of equipment at a time (at most 3).</summary>
    public IState<ImmutableArray<string>> Compared => State.Value(this, () => ImmutableArray<string>.Empty);

    /// <summary>The shared cursor's sample index; a double because the slider binds two-way to it.</summary>
    public IState<double> Cursor => State.Value(this, () => (double)(TrendSet.Samples - 1));

    public IFeed<ViewInputs> Inputs => Feed.Combine(System, Range, Compared, Cursor)
        .Select(t => new ViewInputs(t.Item1, t.Item2, t.Item3, (int)Math.Round(t.Item4)));

    public IFeed<Setpoints> CurrentSetpoints => Feed.Combine(TargetMw, TurbineLimitBar, DamperPct)
        .Select(t => new Setpoints(t.Item1, t.Item2, t.Item3));

    // Feed.Combine takes at most eight sources, so the view inputs arrive already combined.
    public IFeed<ConsoleView> View => Feed.Combine(Snapshot, CurrentSetpoints, Ops, Mode, Selection, Clock, Inputs)
        .Select(t => ConsoleView.Compose(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5.Unit, t.Item6, t.Item7));

    /// <summary>Selects a unit from a callout or a connection chip.</summary>
    public async ValueTask SelectUnit(UnitId unit, PlantSystem system, CancellationToken ct) =>
        await Select(unit, system, ct);

    /// <summary>The equipment picker's choice (the ComboBox passes its selected item).</summary>
    public async ValueTask PickEquipment(ConnectionView? item, PlantSystem system, CancellationToken ct)
    {
        if (item is not null) await Select(item.Id, system, ct);
    }

    /// <summary>
    /// Isolates a system in the plant view (the systems bar passes the enum name). A selection outside
    /// the system moves to its first unit in alert, else its first unit, so the inspector and trends
    /// follow the drawing.
    /// </summary>
    public async ValueTask SelectSystem(string system, UnitSelection selection, ConsoleView view, CancellationToken ct)
    {
        if (!Enum.TryParse<PlantSystem>(system, out var value)) return;
        var unit = PlantSystems.SelectionFor(value, selection.Unit, view.AlertingUnits);
        // Selection first: the picker's list then swaps to the new system with its item already chosen.
        if (unit != selection.Unit) await Selection.UpdateAsync(_ => new UnitSelection(unit), ct);
        await System.SetAsync(value, ct);
    }

    /// <summary>Selects a unit and widens the view to all systems when the unit is outside the one shown.</summary>
    private async ValueTask Select(UnitId unit, PlantSystem system, CancellationToken ct)
    {
        await Selection.UpdateAsync(_ => new UnitSelection(unit), ct);
        var shown = PlantSystems.SystemFor(system, unit);
        if (shown != system) await System.SetAsync(shown, ct);
    }

    public async ValueTask SelectRange(string range, CancellationToken ct)
    {
        if (Enum.TryParse<TrendRange>(range, out var value)) await Range.SetAsync(value, ct);
    }

    /// <summary>
    /// Adds a reading to the trends or takes it out. The trends show one item of equipment at a time,
    /// so readings of another unit are dropped; when nothing was picked for this unit, its first
    /// reading (shown by default) counts as picked. At most three: the oldest gives way.
    /// </summary>
    public async ValueTask ToggleCompare(string key, CancellationToken ct) =>
        await Compared.UpdateAsync(current =>
        {
            if (EquipmentCatalog.Find(key) is null) return current;
            var unit = EquipmentCatalog.UnitOf(key);
            var keys = current.Where(k => EquipmentCatalog.Find(k) is not null && EquipmentCatalog.UnitOf(k) == unit).ToList();
            if (keys.Count == 0) keys.Add(EquipmentCatalog.Get(unit).Readings[0].Key);
            if (!keys.Remove(key)) keys.Add(key);
            while (keys.Count > 3) keys.RemoveAt(0);
            return [.. keys];
        }, ct);

    public async ValueTask SetCursorLatest(CancellationToken ct) =>
        await Cursor.SetAsync(TrendSet.Samples - 1, ct);

    /// <summary>The alert pill: selects the first unit in alert (the view arrives as a feed parameter).</summary>
    public async ValueTask SelectFirstAlert(ConsoleView view, PlantSystem system, CancellationToken ct)
    {
        if (view is { AlertingUnits.IsEmpty: false }) await Select(view.AlertingUnits[0], system, ct);
    }

    // ---------------------------------------------------------------- operations (SPEC-OPERATIONS.md)

    /// <summary>Updates the ops state; MVUX hands a record state over as nullable, and none means the initial plant.</summary>
    private ValueTask Op(Func<PlantOps, PlantOps> change, CancellationToken ct) =>
        Ops.UpdateAsync(o => change(o ?? PlantOps.Initial), ct);
    // Commands read the latest snapshot as a feed parameter (MVUX injects the current value of a feed
    // named like the parameter) for the tick their effects start from.

    /// <summary>Selects a device's start or stop for select-before-operate.</summary>
    public async ValueTask SelectDevice(string id, PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o =>
        {
            var d = o.Device(id);
            OpsCommand command = d.Running ? new StopDevice(d.Unit, d.Id, d.Name) : new StartDevice(d.Unit, d.Id, d.Name);
            return o.Select(command, snapshot.Tick);
        }, ct);

    /// <summary>Selects a valve's move to the other position.</summary>
    public async ValueTask SelectValve(int id, PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o =>
        {
            var valve = o.Valves.First(v => v.Id == id);
            return ConsoleView.ValveOwner(id) is { } owner ? o.Select(new MoveValve(owner, id, !valve.Open), snapshot.Tick) : o;
        }, ct);

    public async ValueTask ConfirmPending(PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o => o.Confirm(snapshot.Tick, _time.GetLocalNow()), ct);

    public async ValueTask CancelPending(CancellationToken ct) =>
        await Op(o => o.Cancel(), ct);

    public async ValueTask Acknowledge(string key, PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o => o.Acknowledge(key, AlarmBook.Title(key), EquipmentCatalog.UnitOf(key), snapshot.Tick, _time.GetLocalNow()), ct);

    public async ValueTask Shelve(string key, PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o => o.Shelve(key, AlarmBook.Title(key), EquipmentCatalog.UnitOf(key), snapshot.Tick, _time.GetLocalNow()), ct);

    /// <summary>Acknowledges every alarm waiting for it (the view arrives as a feed parameter).</summary>
    public async ValueTask AcknowledgeAll(ConsoleView view, PlantSnapshot snapshot, CancellationToken ct) =>
        await Op(o => view.AlarmRows.Where(r => r.CanAck)
            .Aggregate(o, (ops, r) => ops.Acknowledge(r.Key, r.Title, r.Unit, snapshot.Tick, _time.GetLocalNow())), ct);

    public async ValueTask ApplyLoad(double loadDraft, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (double.IsNaN(loadDraft)) return;
        var mw = Math.Clamp(Math.Round(loadDraft), 400, 700);
        await Op(o => o.SetLoad(mw, snapshot.Tick, _time.GetLocalNow()), ct);
        await TargetMw.SetAsync(mw, ct);
    }

    public async ValueTask ApplyFanSpeed(double fanSpeedDraft, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (double.IsNaN(fanSpeedDraft)) return;
        var pct = Math.Clamp(Math.Round(fanSpeedDraft), 60, 100);
        await Op(o => o.SetFanSpeed(pct, snapshot.Tick, _time.GetLocalNow()), ct);
    }

    public async ValueTask ApplyReactive(double reactiveDraft, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (double.IsNaN(reactiveDraft)) return;
        var mvar = Math.Clamp(Math.Round(reactiveDraft), 0, 200);
        await Op(o => o.SetReactive(mvar, snapshot.Tick, _time.GetLocalNow()), ct);
    }

    public async ValueTask ApplyLimit(double limitDraft, double turbineLimitBar, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (double.IsNaN(limitDraft)) return;
        var bar = Math.Clamp(Math.Round(limitDraft), 140, 180);
        await TurbineLimitBar.SetAsync(bar, ct);
        await Op(o => o.Noted(UnitId.Trb, $"Inlet pressure alarm limit {turbineLimitBar:0} → {bar:0} bar", snapshot.Tick, _time.GetLocalNow()), ct);
    }

    public async ValueTask ApplyDamper(double damperDraft, double damperPct, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (double.IsNaN(damperDraft)) return;
        var pct = Math.Clamp(Math.Round(damperDraft), 0, 100);
        await DamperPct.SetAsync(pct, ct);
        await Op(o => o.Noted(UnitId.Stk, $"Stack damper {damperPct:0} → {pct:0} %", snapshot.Tick, _time.GetLocalNow()), ct);
    }

    public async ValueTask AddNote(string noteDraft, PlantSnapshot snapshot, CancellationToken ct)
    {
        await Op(o => o.AddNote(noteDraft, snapshot.Tick, _time.GetLocalNow()), ct);
        await NoteDraft.SetAsync("", ct);
    }

    public async ValueTask SetAuto(PlantSnapshot snapshot, ControlMode mode, CancellationToken ct) => await SetMode(ControlMode.Auto, mode, snapshot, ct);

    public async ValueTask SetManual(PlantSnapshot snapshot, ControlMode mode, CancellationToken ct) => await SetMode(ControlMode.Manual, mode, snapshot, ct);

    public async ValueTask SetHold(PlantSnapshot snapshot, ControlMode mode, CancellationToken ct) => await SetMode(ControlMode.Hold, mode, snapshot, ct);

    private async ValueTask SetMode(ControlMode next, ControlMode current, PlantSnapshot snapshot, CancellationToken ct)
    {
        if (next == current) return;
        // HOLD pauses the stream at the source, so the last snapshot stays current and ages.
        _telemetry.IsPaused = next == ControlMode.Hold;
        await Mode.SetAsync(next, ct);
        await Op(o => o.Logged(snapshot.Tick, _time.GetLocalNow(), LogKind.Action, null, $"Plant mode {current} → {next}"), ct);
    }

    private async IAsyncEnumerable<DateTimeOffset> Ticks([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return _time.GetLocalNow();
            await Task.Delay(TimeSpan.FromSeconds(1), _time, ct);
        }
    }
}

public sealed record UnitSelection(UnitId? Unit);
