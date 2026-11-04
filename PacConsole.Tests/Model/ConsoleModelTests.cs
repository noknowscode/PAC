using PacConsole.Core.Console;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;
using PacConsole.Presentation;

namespace PacConsole.Tests.Model;

/// <summary>
/// The page model: its telemetry stream and Retry, HOLD, the Apply commands and their order, selection, systems and
/// the theme. Driven through the model's own states and commands, the way the page's bindings drive them.
/// </summary>
public class ConsoleModelTests
{
    [Test]
    public async Task The_console_opens_on_the_cooling_bank_at_the_first_snapshot()
    {
        await using var h = new ModelHarness();

        var view = await h.Read(h.Model.View);

        view!.Selected.Should().Be(UnitId.Clg, "the cooling bank is in alert at start and the inspector opens on it");
        view.Tick.Should().Be(Simulation.StartTick);
        view.Mode.Should().Be(ControlMode.Auto);
        (await h.Read(h.Model.Ops))!.Log.Should().BeEmpty("the operator has done nothing yet");
    }

    [Test]
    public async Task A_stream_fault_shows_as_the_feed_s_error_and_Retry_restarts_the_stream()
    {
        await using var h = new ModelHarness();
        h.Telemetry.FaultAfter = 1;
        await using var messages = MessageLog.Of(h.Model.Snapshot, h.Model);

        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Any(m => m.Data is { } s && s.Tick == Simulation.StartTick)), "the first snapshot arrives");
        await h.TickAsync();
        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Last().Error is TelemetryFaultException), "the fault reaches the feed");

        await h.Model.RetryTelemetry();
        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Last() is { Error: null, Data: not null }), "Retry brings data back");
        await h.TickAsync();
        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Last().Data?.Tick > Simulation.StartTick), "the new stream advances");
        messages.Last().Error.Should().BeNull("a successful snapshot clears the error");
    }

    [Test]
    public async Task A_stream_that_ends_without_a_snapshot_is_the_none_state_not_an_error()
    {
        await using var h = new ModelHarness(telemetry: new SilentTelemetry());
        await using var messages = MessageLog.Of(h.Model.Snapshot, h.Model);

        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Any(m => m is { IsNone: true, Error: null })), "the empty stream ends");
    }

    [Test]
    public async Task A_stream_that_ends_after_data_says_the_values_are_no_longer_current()
    {
        var once = new OneSnapshotTelemetry();
        await using var h = new ModelHarness(telemetry: once);
        await using var messages = MessageLog.Of(h.Model.Snapshot, h.Model);

        await ModelHarness.UntilAsync(() => Task.FromResult(messages.Any(m => m.Error is TelemetryFaultException)), "the ended stream is an error");
        messages.Last().Error!.Message.Should().Contain("ended");
        once.Subscriptions.Should().Be(1);

        await h.Model.RetryTelemetry();
        await ModelHarness.UntilAsync(() => Task.FromResult(once.Subscriptions == 2), "Retry subscribes again");
    }

    [Test]
    public async Task Hold_pauses_the_stream_at_its_source_and_Auto_resumes_it()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.SetHold(snapshot, ControlMode.Auto, default);

        h.Telemetry.IsPaused.Should().BeTrue();
        (await h.Read(h.Model.Mode)).Should().Be(ControlMode.Hold);
        (await h.Read(h.Model.Ops))!.Log.Should().ContainSingle(e => e.Text == "Plant mode Auto → Hold");

        await h.Model.SetAuto(snapshot, ControlMode.Hold, default);

        h.Telemetry.IsPaused.Should().BeFalse();
        (await h.Read(h.Model.Mode)).Should().Be(ControlMode.Auto);
    }

    [Test]
    public async Task Choosing_the_mode_already_in_force_logs_nothing()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.SetAuto(snapshot, ControlMode.Auto, default);

        (await h.Read(h.Model.Ops))!.Log.Should().BeEmpty();
    }

    [Test]
    public async Task A_new_limit_keeps_the_old_one_for_the_past_before_the_new_value_lands()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        await using var views = MessageLog.Of(h.Model.View, h.Model);
        await ModelHarness.UntilAsync(() => Task.FromResult(views.Any(m => m.HasData)), "the first view");

        await h.Model.ApplyLimit(150, ControlMode.Auto, snapshot, default);

        var ops = (await h.Read(h.Model.Ops))!;
        ops.Setpoints.TurbineLimitBar.Should().Be(150);
        ops.SetpointChanges.Should().ContainSingle().Which.Before.TurbineLimitBar.Should().Be(170);
        ops.SetpointsAt(snapshot.Tick, ops.Setpoints).TurbineLimitBar.Should().Be(170, "the snapshot the operator acted on had the old limit");
        await ModelHarness.UntilAsync(() => Task.FromResult(views.Any(m => m.HasData && m.Data!.LimitText == "150 bar")), "the view shows the limit");
        // One state carries the limit and the change that dates it, so no composed view has one without the other: a
        // view with the new limit and no change would back-date a TRB-04 alarm over the whole 15-minute window.
        views.Where(m => m.HasData && m.Data!.LimitText == "150 bar").Should()
            .OnlyContain(m => !m.Data!.AlarmRows.Any(r => r.Key == "trb.press" && r.Time.StartsWith("before", StringComparison.Ordinal)));
    }

    [Test]
    public async Task Load_ramps_in_Auto_and_applies_at_once_in_Manual()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        var tick = snapshot.Tick;

        await h.Model.ApplyLoad(640, ControlMode.Auto, snapshot, default);

        var ops = (await h.Read(h.Model.Ops))!;
        ops.Setpoints.TargetMw.Should().Be(640);
        ops.Load.ValueAt(tick + 1).Should().BeLessThan(640, "Auto ramps at 5 MW a minute");
        ops.Log.Last().Text.Should().Be("Load setpoint 620 → 640 MW (5 MW/min)");

        await h.Model.SetManual(snapshot, ControlMode.Auto, default);
        await h.Model.ApplyFanSpeed(75, ControlMode.Manual, snapshot, default);

        (await h.Read(h.Model.Ops))!.Log.Last().Text.Should().Be("Fan speed setpoint 100 → 75 % (at once, Manual)");
    }

    [Test]
    public async Task A_typed_value_is_rounded_and_clamped_and_an_empty_field_applies_nothing()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.ApplyLimit(120.4, ControlMode.Auto, snapshot, default);
        (await h.Read(h.Model.Ops))!.Setpoints.TurbineLimitBar.Should().Be(140, "below the range is clamped to its minimum");

        await h.Model.ApplyDamper(double.NaN, ControlMode.Auto, snapshot, default);
        (await h.Read(h.Model.Ops))!.Setpoints.DamperPct.Should().Be(65, "an empty NumberBox is NaN, which applies nothing");
        (await h.Read(h.Model.Ops))!.Log.Should().HaveCount(1);

        await h.Model.ApplyReactive(150.6, ControlMode.Auto, snapshot, default);
        (await h.Read(h.Model.Ops))!.Log.Last().Text.Should().Be("Reactive power setpoint 120 → 151 MVAr");
    }

    [Test]
    public async Task Choosing_a_system_moves_the_selection_into_it_before_the_system_changes()
    {
        await using var h = new ModelHarness();
        var view = (await h.Read(h.Model.View))!;
        await h.Model.Selection.UpdateAsync(_ => new UnitSelection(UnitId.Trb), default);
        await using var pairs = MessageLog.Of(Feed.Combine(h.Model.Selection, h.Model.System), h.Model);
        await ModelHarness.UntilAsync(() => Task.FromResult(pairs.Any(m => m.HasData)), "both states have values");

        await h.Model.SelectSystem("Cooling", new UnitSelection(UnitId.Trb), view, default);

        (await h.Read(h.Model.System)).Should().Be(PlantSystem.Cooling);
        (await h.Read(h.Model.Selection))!.Unit.Should().Be(UnitId.Clg, "the first cooling unit in alert");
        await ModelHarness.UntilAsync(() => Task.FromResult(pairs.Any(m => m.HasData && m.Data.Item2 == PlantSystem.Cooling)), "the pair reaches the log");
        pairs.Where(m => m.HasData && m.Data.Item2 == PlantSystem.Cooling).Should().OnlyContain(m => m.Data.Item1!.Unit == UnitId.Clg,
            "the picker swaps to the system's list with its item already chosen");
    }

    [Test]
    public async Task A_system_that_holds_the_selection_keeps_it_and_an_unknown_name_changes_nothing()
    {
        await using var h = new ModelHarness();
        var view = (await h.Read(h.Model.View))!;

        await h.Model.SelectSystem("Cooling", new UnitSelection(UnitId.Clg), view, default);
        (await h.Read(h.Model.Selection))!.Unit.Should().Be(UnitId.Clg);

        await h.Model.SelectSystem("Nonsense", new UnitSelection(UnitId.Clg), view, default);
        (await h.Read(h.Model.System)).Should().Be(PlantSystem.Cooling);
    }

    [Test]
    public async Task Selecting_a_unit_outside_the_shown_system_widens_the_view_to_all_systems()
    {
        await using var h = new ModelHarness();
        var view = (await h.Read(h.Model.View))!;
        await h.Model.System.SetAsync(PlantSystem.Cooling, default);

        await h.Model.SelectUnit(UnitId.Gen, PlantSystem.Cooling, view, default);

        (await h.Read(h.Model.Selection))!.Unit.Should().Be(UnitId.Gen);
        (await h.Read(h.Model.System)).Should().Be(PlantSystem.All);

        await h.Model.PickEquipment(new ConnectionView(UnitId.Cnd, "CND-05"), PlantSystem.All, view, default);
        (await h.Read(h.Model.Selection))!.Unit.Should().Be(UnitId.Cnd);
        await h.Model.PickEquipment(null, PlantSystem.All, view, default);
        (await h.Read(h.Model.Selection))!.Unit.Should().Be(UnitId.Cnd, "a cleared picker selects nothing new");
    }

    [TestCase(AppTheme.Dark, ThemeChoice.Graphite)]
    [TestCase(AppTheme.Light, ThemeChoice.Light)]
    [TestCase(AppTheme.System, ThemeChoice.System)]
    public async Task The_saved_theme_opens_as_its_menu_choice(AppTheme saved, ThemeChoice shown)
    {
        await using var h = new ModelHarness(saved);

        (await h.Read(h.Model.Theme)).Should().Be(shown);
    }

    [TestCase("Graphite", AppTheme.Dark, ThemeChoice.Graphite)]
    [TestCase("Light", AppTheme.Light, ThemeChoice.Light)]
    [TestCase("System", AppTheme.System, ThemeChoice.System)]
    public async Task A_menu_choice_goes_to_the_theme_service_and_the_menu(string choice, AppTheme applied, ThemeChoice shown)
    {
        await using var h = new ModelHarness(AppTheme.Light);

        await h.Model.SetTheme(choice, default);

        h.Themes.Requests.Should().Equal(applied);
        (await h.Read(h.Model.Theme)).Should().Be(shown);
    }

    [Test]
    public async Task An_unknown_theme_name_changes_nothing()
    {
        await using var h = new ModelHarness(AppTheme.Light);

        await h.Model.SetTheme("Purple", default);

        h.Themes.Requests.Should().BeEmpty();
        (await h.Read(h.Model.Theme)).Should().Be(ThemeChoice.Light);
    }

    [Test]
    public async Task Full_screen_goes_to_the_window_service_each_press()
    {
        await using var h = new ModelHarness();

        h.Model.CanGoFullScreen.Should().BeTrue("the header offers the button where the window service can switch");
        await h.Model.ToggleFullScreen();
        await h.Model.ToggleFullScreen();

        h.FullScreen.Toggles.Should().Be(2, "each press switches between full screen and the normal window");
    }

    [Test]
    public async Task Confirm_runs_the_selected_command_and_Cancel_drops_it()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.SelectDevice("clg.cell6", snapshot, default);
        (await h.Read(h.Model.Ops))!.Pending!.Command.Should().BeOfType<StartDevice>();
        await h.Model.ConfirmPending(snapshot, ControlMode.Auto, default);

        var ops = (await h.Read(h.Model.Ops))!;
        ops.Pending.Should().BeNull();
        ops.Device("clg.cell6").Running.Should().BeTrue();
        ops.Log.Last().Text.Should().Be("Start Cell 6 (standby) on CLG-05");

        await h.Model.SelectValve(103, snapshot, default);
        (await h.Read(h.Model.Ops))!.Pending.Should().NotBeNull();
        await h.Model.CancelPending(default);
        (await h.Read(h.Model.Ops))!.Pending.Should().BeNull();
        (await h.Read(h.Model.Ops))!.Valves.Single(v => v.Id == 103).Open.Should().BeFalse();
    }

    [Test]
    public async Task Acknowledge_all_acknowledges_every_alarm_waiting_for_it()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        var view = (await h.Read(h.Model.View))!;
        var waiting = view.AlarmRows.Where(r => r.CanAck).Select(r => r.Key).ToList();
        waiting.Should().NotBeEmpty("the cooling bank is in alarm at start");

        await h.Model.AcknowledgeAll(view, snapshot, ControlMode.Auto, default);

        var ops = (await h.Read(h.Model.Ops))!;
        ops.Acks.Keys.Should().BeEquivalentTo(waiting);
        ops.Log.Should().HaveCount(waiting.Count).And.OnlyContain(e => e.Kind == LogKind.Acknowledge);
    }

    [Test]
    public async Task Shelve_then_Unshelve_is_logged_each_way()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        var key = (await h.Read(h.Model.View))!.AlarmRows.First().Key;

        await h.Model.Shelve(key, snapshot, ControlMode.Auto, default);
        await ModelHarness.UntilAsync(async () => (await h.Read(h.Model.Ops))!.IsShelved(key, snapshot.Tick), "the alarm is shelved");
        await h.Model.Unshelve(key, snapshot, ControlMode.Auto, default);
        await ModelHarness.UntilAsync(async () => !(await h.Read(h.Model.Ops))!.IsShelved(key, snapshot.Tick), "the alarm is back");

        var ops = (await h.Read(h.Model.Ops))!;
        ops.IsShelved(key, snapshot.Tick).Should().BeFalse();
        ops.Log.Select(e => e.Kind).Should().Equal(LogKind.Shelve, LogKind.Unshelve);
    }

    [Test]
    public async Task A_note_is_logged_and_its_field_cleared_and_a_blank_note_is_not()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.AddNote("  Cell 3 bearing noisy  ", snapshot, ControlMode.Auto, default);
        await h.Model.AddNote("   ", snapshot, ControlMode.Auto, default);

        (await h.Read(h.Model.Ops))!.Log.Should().ContainSingle().Which.Text.Should().Be("Cell 3 bearing noisy");
        (await h.Read(h.Model.NoteDraft)).Should().BeNullOrEmpty();
    }

    [Test]
    public async Task Trend_inputs_follow_their_commands()
    {
        await using var h = new ModelHarness();

        await h.Model.SelectRange("Hours8", default);
        await h.Model.SelectRange("Forever", default);
        await h.Model.ToggleCompare("clg.vib", default);
        await h.Model.Cursor.SetAsync(3, default);
        await h.Model.SetCursorLatest(default);

        (await h.Read(h.Model.Range)).Should().Be(TrendRange.Hours8);
        (await h.Read(h.Model.Compared)).Should().Equal(["clg.basin", "clg.vib"], "the reading shown by default counts as picked");
        (await h.Read(h.Model.Cursor)).Should().Be(TrendSet.Samples - 1);
        var inputs = (await h.Read(h.Model.Inputs))!;
        inputs.Range.Should().Be(TrendRange.Hours8);
        inputs.Cursor.Should().Be(TrendSet.Samples - 1);
    }

    [Test]
    public async Task The_held_data_clock_ticks_only_while_the_plant_is_held()
    {
        await using var h = new ModelHarness();
        await using var clock = MessageLog.Of(h.Model.Clock, h.Model);
        await ModelHarness.UntilAsync(() => Task.FromResult(clock.Count(m => m.HasData) == 1), "the clock's first value");

        for (var i = 0; i < 3; i++) await h.TickAsync();
        clock.Select(m => m.Data).Distinct().Count().Should().Be(1, "running, each snapshot carries its own time");

        h.Telemetry.IsPaused = true;
        await h.TickAsync();
        await ModelHarness.UntilAsync(() => Task.FromResult(clock.Select(m => m.Data).Distinct().Count() > 1), "held, the clock ticks");
    }

    /// <summary>A source that ends at once, never sending a snapshot.</summary>
    private sealed class SilentTelemetry : ITelemetryService
    {
        public bool IsPaused { get; set; }

        public async IAsyncEnumerable<PlantSnapshot> Stream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield break;
        }
    }

    /// <summary>A source that sends one snapshot and ends; counts its subscriptions.</summary>
    private sealed class OneSnapshotTelemetry : ITelemetryService
    {
        public int Subscriptions { get; private set; }

        public bool IsPaused { get; set; }

        public async IAsyncEnumerable<PlantSnapshot> Stream([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Subscriptions++;
            await Task.Yield();
            yield return new PlantSnapshot(Simulation.StartTick, DateTimeOffset.UnixEpoch, Simulation.Readings(Simulation.StartTick));
        }
    }
}
