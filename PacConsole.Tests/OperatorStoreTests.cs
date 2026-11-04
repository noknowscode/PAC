using PacConsole.Core.Operations;
using PacConsole.Core.Persistence;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

/// <summary>The operator record on disk: what it keeps, how it is written, and what a launch does with a bad file.</summary>
public class OperatorStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "pac-store-tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>An operator shift's worth of changes: every kind of field the record holds.</summary>
    private static PlantOps Worked()
    {
        var ops = PlantOps.Initial
            .Acknowledge("clg.vib", "Fan vibration high", UnitId.Clg, 130, Now)
            .Shelve("cnd.press", "Condenser pressure high", UnitId.Cnd, 131, Now)
            .Select(new StartDevice(UnitId.Clg, "clg.cell6", "Cell 6 (standby)"), 132);
        ops = ops.Confirm(133, Now)
            .Select(new MoveValve(UnitId.Trb, 103, true), 134).Confirm(135, Now)
            .SetLoad(640, 136, Now)
            .ModeChanged(ControlMode.Auto, ControlMode.Manual, 137, Now)
            .SetFanSpeed(75, 138, Now, ControlMode.Manual)
            .SetTurbineLimit(150, 139, Now)
            .SetDamper(80, 140, Now)
            .AddNote("Cell 3 bearing noisy", 141, Now)
            .Select(new StopDevice(UnitId.Clg, "clg.cell3", "Cell 3"), 142);
        return ops;
    }

    [Test]
    public void A_saved_record_reads_back_whole_without_its_pending_command()
    {
        var ops = Worked();
        ops.Pending.Should().NotBeNull();
        var store = new OperatorStore(_dir);

        store.Save(OperatorRecord.Of(142, Now, ControlMode.Manual, ops));
        var load = store.Load(Now);

        load.SetAside.Should().BeNull();
        var back = load.Record!;
        back.Tick.Should().Be(142);
        back.SavedAt.Should().Be(Now);
        back.Mode.Should().Be(ControlMode.Manual);
        var o = back.Ops;
        o.Pending.Should().BeNull("a relaunch must not leave a command one press from running");
        o.Devices.Should().BeEquivalentTo(ops.Devices);
        o.Valves.Should().Equal(ops.Valves);
        o.ValveMoves.Should().BeEquivalentTo(ops.ValveMoves);
        o.Acks.Should().BeEquivalentTo(ops.Acks);
        o.Shelves.Should().BeEquivalentTo(ops.Shelves);
        o.Log.Should().Equal(ops.Log);
        o.SetpointChanges.Should().Equal(ops.SetpointChanges);
        o.Setpoints.Should().Be(new Setpoints(640, 150, 80));
        o.Load.Should().Be(ops.Load);
        o.Reactive.Should().Be(ops.Reactive);
        o.Silo.Should().Be(ops.Silo);
        o.FanSpeed.Should().Be(ops.FanSpeed);
        o.FanSpeed.IsStep.Should().BeTrue("a Manual step's infinite rate survives the file");
        o.Load.PausedAt.Should().Be(137, "a ramp Manual paused stays paused");
    }

    [Test]
    public void The_record_reads_the_same_plant_after_a_relaunch()
    {
        var ops = Worked() with { Pending = null };
        var store = new OperatorStore(_dir);
        store.Save(OperatorRecord.Of(142, Now, ControlMode.Manual, ops));

        var back = new OperatorStore(_dir).Load(Now).Record!.Ops;

        foreach (var tick in new long[] { 120, 140, 150, 400 })
        {
            Simulation.OutputMw(tick, PlantEffects.OpenValves(back, tick), back).Should().Be(Simulation.OutputMw(tick, PlantEffects.OpenValves(ops, tick), ops));
            Simulation.Readings(tick, back.SetpointsAt(tick, back.Setpoints).DamperPct, back).Should().Equal(Simulation.Readings(tick, ops.SetpointsAt(tick, ops.Setpoints).DamperPct, ops));
        }
        AlarmBook.Compose(400, back.Setpoints, back).Entries.Select(e => (e.Key, e.State))
            .Should().Equal(AlarmBook.Compose(400, ops.Setpoints, ops).Entries.Select(e => (e.Key, e.State)));
    }

    [Test]
    public void A_first_launch_finds_nothing_and_sets_nothing_aside()
    {
        new OperatorStore(_dir).Load(Now).Should().Be(OperatorLoad.Empty);
    }

    [Test]
    public void Each_save_replaces_the_last_whole_and_leaves_no_temporary_file()
    {
        var store = new OperatorStore(_dir);
        store.Save(OperatorRecord.Of(130, Now, ControlMode.Auto, PlantOps.Initial));
        store.Save(OperatorRecord.Of(131, Now, ControlMode.Auto, PlantOps.Initial.AddNote("second", 131, Now)));

        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().Equal(OperatorStore.FileName);
        store.Load(Now).Record!.Ops.Log.Should().ContainSingle().Which.Text.Should().Be("second");
    }

    [Test]
    public void A_write_that_fails_leaves_the_last_good_record()
    {
        var store = new OperatorStore(_dir);
        store.Save(OperatorRecord.Of(130, Now, ControlMode.Auto, PlantOps.Initial.AddNote("kept", 130, Now)));
        // The temporary file's name taken by a folder: the write fails before anything is moved.
        Directory.CreateDirectory(store.FilePath + ".tmp");

        var save = () => store.Save(OperatorRecord.Of(131, Now, ControlMode.Auto, PlantOps.Initial.AddNote("lost", 131, Now)));

        save.Should().Throw<UnauthorizedAccessException>();
        store.Load(Now).Record!.Ops.Log.Single().Text.Should().Be("kept");
    }

    [Test]
    public void A_damaged_file_is_set_aside_and_the_console_starts_fresh()
    {
        Directory.CreateDirectory(_dir);
        var store = new OperatorStore(_dir);
        File.WriteAllText(store.FilePath, "{\"version\":1,\"tick\":12");

        var load = store.Load(Now);

        load.Record.Should().BeNull();
        load.SetAside.Should().EndWith(".unreadable-20261104-090000");
        File.Exists(load.SetAside).Should().BeTrue("the operator's data is kept for whoever can recover it");
        File.Exists(store.FilePath).Should().BeFalse();
        store.Load(Now).Should().Be(OperatorLoad.Empty, "the next launch starts clean");
    }

    [TestCase("{\"version\":2}")]
    [TestCase("null")]
    [TestCase("[]")]
    public void A_file_from_another_version_or_of_another_shape_is_set_aside(string json)
    {
        Directory.CreateDirectory(_dir);
        var store = new OperatorStore(_dir);
        File.WriteAllText(store.FilePath, json);

        var load = store.Load(Now);

        load.Record.Should().BeNull();
        load.SetAside.Should().NotBeNull();
    }

    [Test]
    public void A_record_for_different_equipment_is_set_aside()
    {
        var store = new OperatorStore(_dir);
        store.Save(OperatorRecord.Of(130, Now, ControlMode.Auto, PlantOps.Initial));
        var json = File.ReadAllText(store.FilePath).Replace("\"clg.cell6\"", "\"clg.cell9\"", StringComparison.Ordinal);
        File.WriteAllText(store.FilePath, json);

        store.Load(Now).Record.Should().BeNull();
    }

    [Test]
    public void The_plant_resumes_one_tick_per_interval_since_the_save_and_never_runs_backwards()
    {
        var record = OperatorRecord.Of(500, Now, ControlMode.Auto, PlantOps.Initial);

        record.ResumeTick(Now, SimulatedTelemetry.TickInterval).Should().Be(500);
        record.ResumeTick(Now.AddSeconds(61), SimulatedTelemetry.TickInterval).Should().Be(530);
        record.ResumeTick(Now.AddHours(1), SimulatedTelemetry.TickInterval).Should().Be(500 + 1800, "an hour shelved is over after an hour closed");
        record.ResumeTick(Now.AddMinutes(-5), SimulatedTelemetry.TickInterval).Should().Be(500, "a clock set back");
    }

    [Test]
    public void A_shelf_runs_out_while_the_console_is_closed()
    {
        var ops = PlantOps.Initial.Shelve("clg.vib", "Fan vibration high", UnitId.Clg, 200, Now);
        var record = OperatorRecord.Of(200, Now, ControlMode.Auto, ops);

        var resumed = record.ResumeTick(Now.AddMinutes(61), SimulatedTelemetry.TickInterval);

        ops.IsShelved("clg.vib", resumed).Should().BeFalse();
        ops.IsShelved("clg.vib", record.ResumeTick(Now.AddMinutes(30), SimulatedTelemetry.TickInterval)).Should().BeTrue();
    }
}
