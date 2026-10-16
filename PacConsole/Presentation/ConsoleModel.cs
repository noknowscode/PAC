using PacConsole.Core.Console;
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

    // Operator inputs. Three plain states bind two-way to the sliders.
    public IState<double> TargetMw => State.Value(this, () => Setpoints.Default.TargetMw);
    public IState<double> TurbineLimitBar => State.Value(this, () => Setpoints.Default.TurbineLimitBar);
    public IState<double> DamperPct => State.Value(this, () => Setpoints.Default.DamperPct);

    public IState<ImmutableArray<Valve>> Valves => State.Value(this, () => Valve.Defaults);

    public IState<ControlMode> Mode => State.Value(this, () => ControlMode.Auto);

    /// <summary>
    /// Opens on the cooling bank, the unit in alert at start. Pinned rather than left to follow the
    /// first alert: the cooling bank drifts in and out of alarm, and the inspector must not change
    /// equipment on its own. Wrapped in a record because MVUX treats null as "no value".
    /// </summary>
    public IState<UnitSelection> Selection => State.Value(this, () => new UnitSelection(UnitId.Clg));

    /// <summary>The reading plotted for the selected unit. A value-type state, so it always has a value and never stalls the combine.</summary>
    public IState<Metric> Focus => State.Value(this, () => Metric.Vib);

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
    public IFeed<ConsoleView> View => Feed.Combine(Snapshot, CurrentSetpoints, Valves, Mode, Selection, Clock, Focus, Inputs)
        .Select(t => ConsoleView.Compose(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5.Unit, t.Item6, t.Item7, t.Item8));

    public async ValueTask SelectUnit(UnitId unit, CancellationToken ct) =>
        await Selection.UpdateAsync(_ => new UnitSelection(unit), ct);

    /// <summary>The equipment picker's choice (the ComboBox passes its selected item).</summary>
    public async ValueTask PickEquipment(ConnectionView? item, CancellationToken ct)
    {
        if (item is not null) await Selection.UpdateAsync(_ => new UnitSelection(item.Id), ct);
    }

    /// <summary>Isolates a system in the plant view (the systems bar passes the enum name).</summary>
    public async ValueTask SelectSystem(string system, CancellationToken ct)
    {
        if (Enum.TryParse<PlantSystem>(system, out var value)) await System.SetAsync(value, ct);
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

    public async ValueTask SelectMetric(Metric metric, CancellationToken ct) =>
        await Focus.SetAsync(metric, ct);

    public async ValueTask SelectFirstAlert(CancellationToken ct)
    {
        var view = await View;
        if (view is { AlertingUnits.IsEmpty: false })
        {
            await Selection.UpdateAsync(_ => new UnitSelection(view.AlertingUnits[0]), ct);
        }
    }

    public async ValueTask ToggleValve(int id, CancellationToken ct) =>
        await Valves.UpdateAsync(vs => [.. vs.Select(v => v.Id == id ? v with { Open = !v.Open } : v)], ct);

    public async ValueTask SetAuto(CancellationToken ct) => await SetMode(ControlMode.Auto, ct);

    public async ValueTask SetManual(CancellationToken ct) => await SetMode(ControlMode.Manual, ct);

    public async ValueTask SetHold(CancellationToken ct) => await SetMode(ControlMode.Hold, ct);

    private async ValueTask SetMode(ControlMode mode, CancellationToken ct)
    {
        // HOLD pauses the stream at the source, so the last snapshot stays current and ages.
        _telemetry.IsPaused = mode == ControlMode.Hold;
        await Mode.SetAsync(mode, ct);
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
