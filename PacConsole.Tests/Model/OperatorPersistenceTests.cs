using Microsoft.Extensions.Logging.Abstractions;
using PacConsole.Core.Operations;
using PacConsole.Core.Persistence;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;
using PacConsole.Services;

namespace PacConsole.Tests.Model;

/// <summary>
/// Operator data across a relaunch: the page model saves after every action and opens where the last launch left
/// off, and the app's journal keeps the file (SPEC-OPERATIONS: actions, acknowledgements and the log are durable).
/// </summary>
public class OperatorPersistenceTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "pac-journal-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task Every_operator_action_is_saved_with_its_tick_and_mode_and_newer_saves_carry_higher_versions()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        var key = (await h.Read(h.Model.View))!.AlarmRows.First().Key;

        await h.Model.Acknowledge(key, snapshot, ControlMode.Auto, default);
        await h.Model.SetManual(snapshot, ControlMode.Auto, default);
        await h.Model.ApplyFanSpeed(80, ControlMode.Manual, snapshot, default);
        await h.Model.AddNote("Handover at 14:00", snapshot, ControlMode.Manual, default);

        h.Journal.Saved.Select(s => s.Version).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        var last = h.Journal.Last!;
        last.Tick.Should().Be(snapshot.Tick);
        last.Mode.Should().Be(ControlMode.Manual);
        last.Ops.Acks.Should().ContainKey(key);
        last.Ops.FanSpeed.To.Should().Be(80);
        last.Ops.Log.Select(e => e.Kind).Should().Equal(LogKind.Acknowledge, LogKind.Action, LogKind.Action, LogKind.Note);
        (await h.Read(h.Model.SaveFailed)).Should().BeFalse();
    }

    [Test]
    public async Task Selecting_and_cancelling_a_command_saves_nothing_and_Confirm_saves_without_the_selection()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;

        await h.Model.SelectDevice("clg.cell6", snapshot, default);
        await h.Model.CancelPending(default);
        h.Journal.Saved.Should().BeEmpty();

        await h.Model.SelectDevice("clg.cell6", snapshot, default);
        await h.Model.ConfirmPending(snapshot, ControlMode.Auto, default);
        h.Journal.Last!.Ops.Pending.Should().BeNull();
        h.Journal.Last!.Ops.Device("clg.cell6").Running.Should().BeTrue();
    }

    [Test]
    public async Task The_console_opens_where_the_last_launch_left_it()
    {
        var at = new DateTimeOffset(2026, 11, 4, 8, 59, 0, TimeSpan.Zero);
        var ops = PlantOps.Initial
            .Acknowledge("clg.vib", "Fan vibration high", UnitId.Clg, 300, at)
            .SetLoad(650, 301, at)
            .SetTurbineLimit(160, 302, at)
            .AddNote("Night shift", 303, at);
        var restored = OperatorRecord.Of(303, at, ControlMode.Auto, ops);
        await using var h = new ModelHarness(restored: restored);

        var view = (await h.Read(h.Model.View))!;

        view.Tick.Should().Be(303 + 30, "the plant ran on for the minute the console was closed");
        (await h.Read(h.Model.Ops))!.Log.Should().Equal(ops.Log);
        view.LogRows.Should().Contain(r => r.Text == "Night shift");
        view.LimitText.Should().Be("160 bar");
        (await h.Read(h.Model.LoadDraft)).Should().Be(650, "the field opens on the value in force");
        (await h.Read(h.Model.LimitDraft)).Should().Be(160);
        (await h.Read(h.Model.Mode)).Should().Be(ControlMode.Auto);
        h.Telemetry.IsPaused.Should().BeFalse();
    }

    [Test]
    public async Task A_console_closed_while_held_opens_held()
    {
        var at = new DateTimeOffset(2026, 11, 4, 8, 59, 0, TimeSpan.Zero);
        var ops = PlantOps.Initial.ModeChanged(ControlMode.Auto, ControlMode.Hold, 300, at);
        await using var h = new ModelHarness(restored: OperatorRecord.Of(300, at, ControlMode.Hold, ops));

        (await h.Read(h.Model.Mode)).Should().Be(ControlMode.Hold);
        h.Telemetry.IsPaused.Should().BeTrue("Hold pauses the stream at its source");
        (await h.Read(h.Model.View))!.DataIsLive.Should().BeFalse();
    }

    [Test]
    public async Task A_failed_save_is_reported_and_the_next_good_one_clears_it()
    {
        await using var h = new ModelHarness();
        var snapshot = (await h.Read(h.Model.Snapshot))!;
        h.Journal.Fails = true;

        await h.Model.AddNote("first", snapshot, ControlMode.Auto, default);
        (await h.Read(h.Model.SaveFailed)).Should().BeTrue();
        (await h.Read(h.Model.Ops))!.Log.Should().ContainSingle("the console keeps working from memory");

        h.Journal.Fails = false;
        await h.Model.AddNote("second", snapshot, ControlMode.Auto, default);
        (await h.Read(h.Model.SaveFailed)).Should().BeFalse();
        h.Journal.Last!.Ops.Log.Should().HaveCount(2, "the next save carries everything since");
    }

    [Test]
    public async Task The_journal_keeps_the_record_across_a_relaunch()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var first = new OperatorJournal(time, NullLogger<OperatorJournal>.Instance, _ => Task.FromResult(_dir));
        await first.OpenAsync(default);
        first.Restored.Should().BeNull("a first launch");

        var ops = PlantOps.Initial.AddNote("kept", 130, time.GetLocalNow());
        (await first.SaveAsync(OperatorRecord.Of(130, time.GetUtcNow(), ControlMode.Manual, ops), 2, default)).Should().BeTrue();
        (await first.SaveAsync(OperatorRecord.Of(129, time.GetUtcNow(), ControlMode.Auto, PlantOps.Initial), 1, default))
            .Should().BeTrue("an older save that arrives late is skipped, not an error");

        var relaunch = new OperatorJournal(time, NullLogger<OperatorJournal>.Instance, _ => Task.FromResult(_dir));
        await relaunch.OpenAsync(default);

        relaunch.Restored!.Mode.Should().Be(ControlMode.Manual);
        relaunch.Restored.Ops.Log.Single().Text.Should().Be("kept");
    }

    [Test]
    public async Task The_journal_reports_a_save_it_could_not_write()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var journal = new OperatorJournal(time, NullLogger<OperatorJournal>.Instance, _ => Task.FromResult(_dir));
        await journal.OpenAsync(default);
        Directory.CreateDirectory(Path.Combine(_dir, OperatorStore.FileName + ".tmp"));

        (await journal.SaveAsync(OperatorRecord.Of(130, time.GetUtcNow(), ControlMode.Auto, PlantOps.Initial), 1, default)).Should().BeFalse();
    }

    [Test]
    public async Task A_relaunch_through_the_model_restores_what_the_operator_did()
    {
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var journal = new OperatorJournal(time, NullLogger<OperatorJournal>.Instance, _ => Task.FromResult(_dir));
        await journal.OpenAsync(default);
        await using (var h = new ModelHarness(time: time, journal: journal))
        {
            var snapshot = (await h.Read(h.Model.Snapshot))!;
            var view = (await h.Read(h.Model.View))!;
            await h.Model.AcknowledgeAll(view, snapshot, ControlMode.Auto, default);
            await h.Model.ApplyLoad(640, ControlMode.Auto, snapshot, default);
            await h.Model.AddNote("Before the relaunch", snapshot, ControlMode.Auto, default);
        }

        time.Advance(TimeSpan.FromMinutes(1));
        var reopened = new OperatorJournal(time, NullLogger<OperatorJournal>.Instance, _ => Task.FromResult(_dir));
        await reopened.OpenAsync(default);
        await using var after = new ModelHarness(time: time, journal: reopened);
        var restored = (await after.Read(after.Model.View))!;

        restored.Tick.Should().Be(Simulation.StartTick + 30);
        restored.AlarmRows.Where(r => r.Unit == UnitId.Clg).Should().OnlyContain(r => !r.CanAck, "the acknowledgements were kept");
        restored.LogRows.Should().Contain(r => r.Text == "Before the relaunch");
        restored.TargetText.Should().Contain("640");
    }
}
