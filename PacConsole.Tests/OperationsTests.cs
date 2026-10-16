using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

/// <summary>Operations (SPEC-OPERATIONS.md): select-before-operate, permissives, ramps, valves, effects and the alarm book.</summary>
public class OperationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);

    private static PlantOps Do(PlantOps ops, OpsCommand command, long tick) => ops.Select(command, tick).Confirm(tick, Now);

    private static StopDevice Stop(string id) => new(Unit(id), id, id);
    private static StartDevice Start(string id) => new(Unit(id), id, id);
    private static UnitId Unit(string id) => PlantOps.Initial.Device(id).Unit;

    [Test]
    public void A_selected_command_waits_for_confirm_then_runs_and_is_logged()
    {
        var selected = PlantOps.Initial.Select(Start("clg.cell6"), 200);
        selected.Pending!.Refusal.Should().BeNull();
        selected.Device("clg.cell6").Running.Should().BeFalse("nothing runs until Confirm");

        var done = selected.Confirm(201, Now);
        done.Pending.Should().BeNull();
        done.Device("clg.cell6").Should().Match<Device>(d => d.Running && d.ChangedTick == 201);
        done.Device("clg.cell6").StateAt(202).Should().Be("Starting…");
        done.Device("clg.cell6").StateAt(205).Should().Be("Running");
        done.Log.Should().ContainSingle(e => e.Kind == LogKind.Action && e.Text == "Start clg.cell6 on CLG-05" && e.By == "Operator 04");
    }

    [Test]
    public void A_selection_lapses_after_ten_seconds()
    {
        var lapsed = PlantOps.Initial.Select(Start("clg.cell6"), 200).Confirm(206, Now);
        lapsed.Device("clg.cell6").Running.Should().BeFalse();
        lapsed.Pending.Should().BeNull();
        lapsed.Log.Should().BeEmpty();
    }

    [Test]
    public void Cancel_drops_the_selection()
    {
        PlantOps.Initial.Select(Start("clg.cell6"), 200).Cancel().Pending.Should().BeNull();
    }

    [Test]
    public void The_cooling_bank_keeps_four_cells_running()
    {
        var four = Do(PlantOps.Initial, Stop("clg.cell1"), 200);
        four.DevicesOf(UnitId.Clg).Count(d => d.Running).Should().Be(4);
        var refused = four.Select(Stop("clg.cell2"), 201);
        refused.Pending!.Refusal.Should().StartWith("Keep at least 4 cells running");
        var after = refused.Confirm(201, Now);
        after.Device("clg.cell2").Running.Should().BeTrue();
        after.Log.Last().Kind.Should().Be(LogKind.Refused);
    }

    [TestCase("cnd.cwA")]
    [TestCase("tka.p1")]
    public void The_last_running_pump_cannot_be_stopped(string pump)
    {
        PlantOps.Initial.Select(Stop(pump), 200).Pending!.Refusal.Should().NotBeNull();
    }

    [Test]
    public void Swapping_pumps_works_when_the_other_starts_first()
    {
        var ops = Do(Do(PlantOps.Initial, Start("cnd.cwB"), 200), Stop("cnd.cwA"), 210);
        ops.Device("cnd.cwA").Running.Should().BeFalse();
        for (long t = 210; t < 400; t++) PlantEffects.CoolingWaterFlow(ops, t).Should().BeGreaterThan(0.99, $"tick {t}");
    }

    [Test]
    public void One_turbine_admission_valve_stays_open()
    {
        // V-101 and V-102 open, V-103 shut by default.
        var one = Do(PlantOps.Initial, new MoveValve(UnitId.Trb, 101, false), 200);
        one.Valves.Single(v => v.Id == 101).Open.Should().BeFalse();
        one.Select(new MoveValve(UnitId.Trb, 102, false), 201).Pending!.Refusal.Should().Contain("admission valve");
    }

    [Test]
    public void Valves_travel_for_five_ticks_and_output_follows()
    {
        var ops = Do(PlantOps.Initial, new MoveValve(UnitId.Trb, 103, true), 300);
        var v103 = ops.Valves.Single(v => v.Id == 103);
        PlantEffects.ValveState(ops, v103, 299).Should().Be("Shut");
        PlantEffects.ValveState(ops, v103, 302).Should().Be("Opening…");
        PlantEffects.ValveOpening(ops, v103, 302).Should().BeApproximately(0.4, 1e-9);
        PlantEffects.ValveState(ops, v103, 305).Should().Be("Open");
        PlantEffects.OpenValves(ops, 305).Should().Be(8);
        (Simulation.OutputMw(305, PlantEffects.OpenValves(ops, 305), ops) - Simulation.OutputMw(305, 7, PlantOps.Initial)).Should().BeApproximately(6, 1e-9);
    }

    [Test]
    public void Load_ramps_at_five_mw_a_minute_and_output_follows()
    {
        var ops = PlantOps.Initial.SetLoad(640, 200, Now);
        ops.Load.ValueAt(200).Should().Be(620);
        ops.Load.ValueAt(230).Should().BeApproximately(625, 1e-9, "30 ticks are a minute");
        ops.Load.ValueAt(400).Should().Be(640);
        ops.Load.MinutesLeft(200).Should().Be(4);
        (Simulation.OutputMw(400, 7, ops) - Simulation.OutputMw(400, 7, PlantOps.Initial)).Should().BeApproximately(20, 1e-9);
        ops.Log.Single().Text.Should().Be("Load setpoint 620 → 640 MW (5 MW/min)");
    }

    [Test]
    public void The_initial_plant_reads_exactly_as_before_operations()
    {
        // With no actions the cooling bank's vibration is the fault drift itself.
        for (long t = 0; t < 600; t += 7)
        {
            Simulation.Readings(t).First(r => r.Id == UnitId.Clg).Vib.Should().BeApproximately(Simulation.Vibration(UnitId.Clg, t), 1e-9);
            PlantEffects.CoolingDeficit(PlantOps.Initial, t).Should().Be(0);
            PlantEffects.VacuumLossMw(PlantOps.Initial, t).Should().Be(0);
        }
    }

    [Test]
    public void The_story_standby_cell_then_stop_cell_three_clears_the_vibration_alarm()
    {
        var setpoints = Setpoints.Default;
        AlarmRules.At(120, setpoints, PlantOps.Initial).Should().Contain(a => a.Key == "clg.vib");

        var ops = PlantOps.Initial.Acknowledge("clg.vib", "CLG-05 cooling fan vibration high", UnitId.Clg, 121, Now);
        AlarmBook.Compose(121, setpoints, ops).Entries.Single(e => e.Key == "clg.vib").State.Should().Be(AlarmState.Acknowledged);

        ops = Do(ops, Start("clg.cell6"), 122);
        ops = Do(ops, Stop("clg.cell3"), 130);

        // Cell 3 spins down with a 20 s time constant: the alarm is gone well within a minute and stays gone.
        for (long t = 160; t < 1200; t++)
        {
            AlarmRules.At(t, setpoints, ops).Should().NotContain(a => a.Key == "clg.vib" && a.Raised, $"tick {t}");
        }
        PlantEffects.CoolingDeficit(ops, 160).Should().BeLessThan(0.05, "cell 6 replaces cell 3");

        var book = AlarmBook.Compose(160, setpoints, ops);
        book.Entries.Should().NotContain(e => e.Key == "clg.vib", "it was acknowledged before it returned to normal");
        book.Transitions.Should().Contain(t => t.Key == "clg.vib" && !t.Raised);
        ops.Log.Select(e => e.Kind).Should().Equal(LogKind.Acknowledge, LogKind.Action, LogKind.Action);
    }

    [Test]
    public void Reducing_fan_speed_to_seventy_five_percent_also_clears_it_and_warms_the_water()
    {
        var ops = PlantOps.Initial.SetFanSpeed(75, 120, Now);
        for (long t = 145; t < 1000; t++)
        {
            AlarmRules.At(t, Setpoints.Default, ops).Should().NotContain(a => a.Key == "clg.vib" && a.Raised, $"tick {t}");
        }
        PlantEffects.CoolingDeficit(ops, 200).Should().BeApproximately(1.25, 1e-9, "five cells at 75 % are 3.75 cells' worth");
        var book = AlarmBook.Compose(600, Setpoints.Default, ops);
        book.Entries.Should().NotContain(e => e.Key == "clg.vib" && e.IsActive, "75 % keeps cell 3 below the 4.0 mm/s clear threshold");
    }

    [Test]
    public void An_alarm_that_returns_unacknowledged_stays_until_acknowledged()
    {
        var ops = Do(Do(PlantOps.Initial, Start("clg.cell6"), 122), Stop("clg.cell3"), 130);
        var book = AlarmBook.Compose(200, Setpoints.Default, ops);
        book.Entries.Single(e => e.Key == "clg.vib").State.Should().Be(AlarmState.ReturnedUnacknowledged);

        var acked = ops.Acknowledge("clg.vib", "x", UnitId.Clg, 201, Now);
        AlarmBook.Compose(201, Setpoints.Default, acked).Entries.Should().NotContain(e => e.Key == "clg.vib");
    }

    [Test]
    public void Shelving_hides_an_alarm_for_an_hour()
    {
        var ops = PlantOps.Initial.Shelve("clg.vib", "x", UnitId.Clg, 120, Now);
        var book = AlarmBook.Compose(121, Setpoints.Default, ops);
        book.ShelvedCount.Should().Be(1);
        book.ActiveCount.Should().Be(0);
        ops.IsShelved("clg.vib", 120 + PlantOps.ShelveTicks + 1).Should().BeFalse();
    }

    [Test]
    public void Losing_cooling_water_raises_the_condenser_vacuum_alarm_and_costs_output()
    {
        // Both pumps stopped (past the interlock, as a pump trip would): pressure climbs toward 18 kPa.
        var tripped = PlantOps.Initial with
        {
            Devices = PlantOps.Initial.Devices.SetItem("cnd.cwA", PlantOps.Initial.Device("cnd.cwA") with { Running = false, ChangedTick = 200 }),
        };
        AlarmRules.At(200, Setpoints.Default, tripped).Should().NotContain(a => a.Key == "cnd.vacuum" && a.Raised);
        AlarmRules.At(260, Setpoints.Default, tripped).Should().Contain(a => a.Key == "cnd.vacuum" && a.Priority == AlarmPriority.High);
        PlantEffects.VacuumLossMw(tripped, 400).Should().BeGreaterThan(30);
    }

    [Test]
    public void The_silo_level_stays_continuous_when_unloading_starts()
    {
        var before = PlantEffects.SiloLevel(PlantOps.Initial, 500);
        var ops = Do(PlantOps.Initial, Start("sil.unload"), 500);
        PlantEffects.SiloLevel(ops, 500).Should().BeApproximately(before, 1e-9);
        PlantEffects.SiloLevel(ops, 510).Should().BeApproximately(before - 1, 1e-9);
    }

    [Test]
    public void Notes_are_logged_trimmed_and_blank_ones_ignored()
    {
        PlantOps.Initial.AddNote("  ", 200, Now).Log.Should().BeEmpty();
        PlantOps.Initial.AddNote(" Fan 3 inspection booked ", 200, Now).Log.Single().Should()
            .Match<LogEntry>(e => e.Kind == LogKind.Note && e.Text == "Fan 3 inspection booked");
    }
}
