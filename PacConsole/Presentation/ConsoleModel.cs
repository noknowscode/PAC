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
    /// Unit is null until the operator picks one; the view then defaults to the first alert.
    /// Wrapped in a record because MVUX treats a null value as "no value", which would stall the combine.
    /// </summary>
    public IState<UnitSelection> Selection => State.Value(this, () => new UnitSelection(null));

    public IFeed<Setpoints> CurrentSetpoints => Feed.Combine(TargetMw, TurbineLimitBar, DamperPct)
        .Select(t => new Setpoints(t.Item1, t.Item2, t.Item3));

    public IFeed<ConsoleView> View => Feed.Combine(Snapshot, CurrentSetpoints, Valves, Mode, Selection, Clock)
        .Select(t => ConsoleView.Compose(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5.Unit, t.Item6));

    public async ValueTask SelectUnit(UnitId unit, CancellationToken ct) =>
        await Selection.UpdateAsync(_ => new UnitSelection(unit), ct);

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
