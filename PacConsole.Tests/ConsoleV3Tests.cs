using PacConsole.Core.Console;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

/// <summary>The console page v3 (SPEC-CONSOLE.md): equipment, systems, inspector and synchronized trends.</summary>
public class ConsoleV3Tests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 19, 11, 5, TimeSpan.Zero);

    private static ConsoleView Compose(UnitId? selected = null, ViewInputs? inputs = null) =>
        ConsoleView.Compose(
            new PlantSnapshot(Simulation.StartTick, T0, Simulation.Readings(Simulation.StartTick)),
            Setpoints.Default,
            PlantOps.Initial,
            ControlMode.Auto,
            selected,
            T0,
            inputs: inputs);

    private static ConsoleView ComposeWith(PlantOps ops, UnitId selected) =>
        ConsoleView.Compose(
            new PlantSnapshot(Simulation.StartTick, T0, Simulation.Readings(Simulation.StartTick)),
            Setpoints.Default, ops, ControlMode.Auto, selected, T0);

    [Test]
    public void Theme_choice_checks_exactly_one_menu_item()
    {
        Compose().IsThemeSystem.Should().BeTrue("the console follows the OS until the operator picks a theme");
        var graphite = Compose(inputs: ViewInputs.Default with { Theme = ThemeChoice.Graphite });
        (graphite.IsThemeSystem, graphite.IsThemeLight, graphite.IsThemeGraphite).Should().Be((false, false, true));
    }

    [Test]
    public void Every_unit_has_equipment_with_unique_reading_keys()
    {
        EquipmentCatalog.All.Select(e => e.Id).Should().BeEquivalentTo(Enum.GetValues<UnitId>());
        var keys = EquipmentCatalog.All.SelectMany(e => e.Readings).Select(r => r.Key).ToList();
        keys.Should().OnlyHaveUniqueItems();
        foreach (var e in EquipmentCatalog.All)
        {
            e.Readings.Should().Contain(r => r.Group == ReadingGroup.Overview, e.Info.Code);
            e.Readings.Should().Contain(r => r.Group == ReadingGroup.Condition, e.Info.Code);
            e.Readings.Count(r => r.Group == ReadingGroup.Overview).Should().BeLessThanOrEqualTo(6, "the inspector has six overview slots");
            e.Readings.Count(r => r.Group == ReadingGroup.Condition).Should().BeLessThanOrEqualTo(3, "and three condition slots");
            e.Connections.Should().HaveCountLessThanOrEqualTo(2, "two connection chips");
        }
    }

    [Test]
    public void Connections_are_mutual_where_a_pipe_joins_two_units()
    {
        EquipmentCatalog.Get(UnitId.Cnd).Connections.Should().Equal(UnitId.Trb, UnitId.Clg);
        EquipmentCatalog.Get(UnitId.Trb).Connections.Should().Contain(UnitId.Cnd);
        EquipmentCatalog.Get(UnitId.Clg).Connections.Should().Contain(UnitId.Cnd);
    }

    [Test]
    public void Alarms_live_on_the_readings_that_show_them()
    {
        EquipmentCatalog.Find("clg.vib")!.Alarm!.Priority.Should().Be(AlarmPriority.Medium);
        EquipmentCatalog.Find("trb.press")!.Alarm!.Priority.Should().Be(AlarmPriority.High);
        EquipmentCatalog.UnitOf("cnd.ttd").Should().Be(UnitId.Cnd);
    }

    [Test]
    public void The_alarm_band_lists_the_cooling_bank_unacknowledged_with_its_response()
    {
        var view = Compose(UnitId.Clg);
        view.AlarmSummaryText.Should().Be("1 active · 1 unacknowledged");
        view.Alarm0!.Should().Match<AlarmRowView>(r => r.Key == "clg.vib" && r.Priority == "Medium" && r.State == "Unacknowledged" && r.CanAck);
        view.Alarm0.Title.Should().Be("CLG-05 cooling fan vibration high");
        view.Response!.Steps.Should().StartWith("1. Start the standby cell 6.").And.Contain("75 %");
        view.CanAckAll.Should().BeTrue();
    }

    [Test]
    public void The_faceplate_lists_the_selected_units_devices_and_setpoints()
    {
        var clg = Compose(UnitId.Clg);
        clg.Devices.Select(d => d.Name).Should().Equal("Cell 1", "Cell 2", "Cell 3", "Cell 4", "Cell 5", "Cell 6 (standby)");
        clg.Device5!.Should().Match<DeviceView>(d => d.State == "Stopped" && d.Action == "Start");
        clg.ShowFanSpeed.Should().BeTrue();
        clg.FanSpeedText.Should().Be("100 %");
        var trb = Compose(UnitId.Trb);
        trb.Devices.Should().BeEmpty();
        trb.LoadText.Should().Be("620 MW");
        trb.LimitText.Should().Be("170 bar");
    }

    [Test]
    public void A_pending_command_reads_as_a_question_and_a_refusal_as_a_reason()
    {
        var pending = ComposeWith(PlantOps.Initial.Select(new StartDevice(UnitId.Clg, "clg.cell6", "Cell 6 (standby)"), Simulation.StartTick), UnitId.Clg);
        pending.Pending!.Should().Match<PendingView>(p => !p.IsRefused && p.Text == "Confirm: start Cell 6 (standby) on CLG-05?");
        var refused = ComposeWith(PlantOps.Initial.Select(new StopDevice(UnitId.Cnd, "cnd.cwA", "CW pump A"), Simulation.StartTick), UnitId.Cnd);
        refused.Pending!.IsRefused.Should().BeTrue();
        refused.Pending.Reason.Should().Contain("condenser vacuum");
    }

    [Test]
    public void Reading_tiles_carry_their_range_bar()
    {
        var tile = Compose(UnitId.Clg).Condition1!;
        tile.Key.Should().Be("clg.vib");
        tile.LimitAt.Should().BeApproximately(4.5 / 7.1, 1e-9);
        tile.BandStart.Should().Be(0);
        tile.OutOfBand.Should().BeTrue("5 mm/s is above the 2.8 mm/s normal band");
    }

    [TestCase(PlantSystem.Cooling, new[] { UnitId.Clg, UnitId.Cnd })]
    [TestCase(PlantSystem.Electrical, new[] { UnitId.Gen, UnitId.Trb })]
    [TestCase(PlantSystem.FuelFlue, new[] { UnitId.Stk, UnitId.Tka, UnitId.Sil })]
    public void Systems_contain_their_equipment(PlantSystem system, UnitId[] expected)
    {
        Enum.GetValues<UnitId>().Where(u => PlantSystems.Contains(system, u)).Should().BeEquivalentTo(expected);
        Enum.GetValues<UnitId>().Should().OnlyContain(u => PlantSystems.Contains(PlantSystem.All, u));
    }

    [Test]
    public void Active_alarms_holds_exactly_the_units_in_alarm()
    {
        var parts = PacConsole.Core.Solids.PlantSolids.Default.All;
        UnitId[] alerting = [UnitId.Clg, UnitId.Gen];
        parts.Where(p => p.Unit is { } u && alerting.Contains(u))
            .Should().OnlyContain(p => PacConsole.Core.Solids.PlantSolids.InSystem(p, PlantSystem.Alarms, alerting));
        parts.Where(p => p.Unit is { } u && !alerting.Contains(u))
            .Should().NotContain(p => PacConsole.Core.Solids.PlantSolids.InSystem(p, PlantSystem.Alarms, alerting));
        parts.Where(p => p.Name.StartsWith("stand.", StringComparison.Ordinal))
            .Should().OnlyContain(p => PacConsole.Core.Solids.PlantSolids.InSystem(p, PlantSystem.Alarms, alerting));

        // Choosing it moves the selection to the first unit in alarm; with nothing in alarm it stays put.
        PlantSystems.SelectionFor(PlantSystem.Alarms, UnitId.Trb, alerting).Should().Be(UnitId.Gen);
        PlantSystems.SelectionFor(PlantSystem.Alarms, UnitId.Trb, []).Should().Be(UnitId.Trb);
        // Selecting a unit not in alarm widens the view to all systems.
        PlantSystems.SystemFor(PlantSystem.Alarms, UnitId.Trb, alerting).Should().Be(PlantSystem.All);
        PlantSystems.SystemFor(PlantSystem.Alarms, UnitId.Clg, alerting).Should().Be(PlantSystem.Alarms);
    }

    [Test]
    public void Every_system_keeps_the_stand_and_owns_some_of_the_drawing()
    {
        var parts = PacConsole.Core.Solids.PlantSolids.Default.All;
        // Active alarms owns only what is in alarm; it has its own test below.
        foreach (var system in Enum.GetValues<PlantSystem>().Where(s => s != PlantSystem.Alarms))
        {
            parts.Where(p => p.Name.StartsWith("stand.", StringComparison.Ordinal))
                .Should().OnlyContain(p => PacConsole.Core.Solids.PlantSolids.InSystem(p, system), system.ToString());
            parts.Count(p => !p.Name.StartsWith("stand.", StringComparison.Ordinal) && PacConsole.Core.Solids.PlantSolids.InSystem(p, system))
                .Should().BeGreaterThan(5, system.ToString());
        }
        parts.Where(p => p.Name == "cnd.exhaust").Should().OnlyContain(p => PacConsole.Core.Solids.PlantSolids.InSystem(p, PlantSystem.SteamWater));
        parts.Where(p => p.Name.StartsWith("xfmr.", StringComparison.Ordinal)).Should().OnlyContain(p => !PacConsole.Core.Solids.PlantSolids.InSystem(p, PlantSystem.Cooling));
    }

    [TestCase(PlantSystem.Cooling, UnitId.Gen, UnitId.Clg)]
    [TestCase(PlantSystem.Cooling, UnitId.Cnd, UnitId.Cnd)]
    [TestCase(PlantSystem.Electrical, UnitId.Clg, UnitId.Gen)]
    [TestCase(PlantSystem.FuelFlue, null, UnitId.Stk)]
    [TestCase(PlantSystem.All, UnitId.Sil, UnitId.Sil)]
    public void Isolating_a_system_keeps_or_moves_the_selection(PlantSystem system, UnitId? current, UnitId expected)
    {
        PlantSystems.SelectionFor(system, current).Should().Be(expected);
    }

    [Test]
    public void Isolating_a_system_prefers_a_unit_in_alert()
    {
        PlantSystems.SelectionFor(PlantSystem.Electrical, UnitId.Clg, [UnitId.Trb, UnitId.Clg]).Should().Be(UnitId.Trb);
        PlantSystems.SelectionFor(PlantSystem.Electrical, UnitId.Clg, [UnitId.Clg]).Should().Be(UnitId.Gen);
        PlantSystems.SelectionFor(PlantSystem.SteamWater, UnitId.Stk, [UnitId.Cnd]).Should().Be(UnitId.Cnd);
        // A selection already inside the system stays, even with an alert elsewhere in it.
        PlantSystems.SelectionFor(PlantSystem.Electrical, UnitId.Gen, [UnitId.Trb]).Should().Be(UnitId.Gen);
    }

    [TestCase(PlantSystem.Cooling, UnitId.Cnd, PlantSystem.Cooling)]
    [TestCase(PlantSystem.Cooling, UnitId.Gen, PlantSystem.All)]
    [TestCase(PlantSystem.All, UnitId.Gen, PlantSystem.All)]
    public void Selecting_outside_the_system_widens_the_view(PlantSystem current, UnitId unit, PlantSystem expected)
    {
        PlantSystems.SystemFor(current, unit).Should().Be(expected);
    }

    [Test]
    public void The_picker_lists_the_shown_system_only()
    {
        var cooling = Compose(UnitId.Cnd, ViewInputs.Default with { System = PlantSystem.Cooling });
        cooling.EquipmentChoices.Select(c => c.Id).Should().Equal(UnitId.Clg, UnitId.Cnd);
        cooling.SelectedIndex.Should().Be(1);
        Compose(UnitId.Cnd).EquipmentChoices.Should().HaveCount(7);
        // One shared list per system, so the picker's ItemsSource never resets between refreshes.
        Compose(UnitId.Clg, ViewInputs.Default with { System = PlantSystem.Cooling }).EquipmentChoices
            .Should().BeSameAs(cooling.EquipmentChoices);
    }

    [Test]
    public void Status_strip_reads_like_the_mock()
    {
        var view = Compose();
        view.ActiveAlertText.Should().Be("1 active alarm");
        view.TargetText.Should().Be("Target 620 MW");
        view.TargetGapText.Should().MatchRegex(@"^(On target|\d+ MW (below|above) target)$");
        view.ModeLine.Should().Be("Generating · Automatic");
        view.SnapshotText.Should().StartWith("Snapshot ");
    }

    [Test]
    public void Inspector_shows_the_selected_equipment_with_its_first_reading_compared()
    {
        var view = Compose(UnitId.Gen);
        view.InspectorCode.Should().Be("GEN-03");
        view.InspectorName.Should().Be("Generator");
        view.OverviewTiles.Should().HaveCount(6);
        view.Overview0!.Key.Should().Be("gen.gross");
        view.Overview0.IsCompared.Should().BeTrue();
        view.OverviewTiles.Skip(1).Should().OnlyContain(t => !t.IsCompared);
        view.Connections.Select(c => c.Title).Should().Equal("TRB-04 · Steam turbine");
        view.EquipmentChoices.Should().HaveCount(7);
        view.EquipmentChoices[view.SelectedIndex].Title.Should().Be("GEN-03 · Generator");
    }

    [Test]
    public void The_alerting_reading_is_marked_high()
    {
        var view = Compose(UnitId.Clg);
        view.ConditionTiles.Single(t => t.Key == "clg.vib").IsHigh.Should().BeTrue();
        view.OverviewTiles.Should().OnlyContain(t => !t.IsHigh);
        view.InspectorState.Should().Be("Alert");
    }

    [Test]
    public void Trends_lead_with_net_generation_then_the_compared_readings()
    {
        var inputs = ViewInputs.Default with { Compared = ["gen.frequency", "gen.gross", "trb.press"] };
        var view = Compose(UnitId.Gen, inputs);
        view.Trends!.Series.Select(s => s.Key).Should().Equal("net", "gen.frequency", "gen.gross");
        view.Trend0!.Target.Should().Be(Setpoints.Default.TargetMw);
        view.Trend3.Should().BeNull();
        view.CursorIsLatest.Should().BeTrue();
        foreach (var s in view.Trends.Series)
        {
            s.Values.Should().HaveCount(TrendSet.Samples);
            s.Values.Should().OnlyContain(v => v >= s.Min && v <= s.Max, s.Key);
        }
    }

    [Test]
    public void Cursor_moves_the_shared_values_and_time()
    {
        var latest = Compose(UnitId.Gen);
        var earlier = Compose(UnitId.Gen, ViewInputs.Default with { Cursor = 10 });
        earlier.CursorIndex.Should().Be(10);
        earlier.CursorIsLatest.Should().BeFalse();
        earlier.CursorTime.Should().NotBe(latest.CursorTime);
        earlier.Trend0Value.Should().Be(earlier.Trend0!.Format1(earlier.Trend0.Values[10]));
    }

    [TestCase(TrendRange.Minutes15, 15)]
    [TestCase(TrendRange.Hour1, 60)]
    [TestCase(TrendRange.Hours8, 480)]
    public void Range_sets_the_time_span(TrendRange range, int minutes)
    {
        var trends = Compose(inputs: ViewInputs.Default with { Range = range }).Trends!;
        (trends.Times[^1] - trends.Times[0]).TotalMinutes.Should().BeApproximately(minutes, minutes * 0.02);
    }

    [Test]
    public void Eight_hours_carry_the_cooling_bank_alert_onsets()
    {
        var view = Compose(inputs: ViewInputs.Default with { Range = TrendRange.Hours8 });
        view.Trends!.Events.Should().NotBeEmpty();
        view.EventText.Should().Contain("CLG-05");
    }

    [Test]
    public void Valves_show_only_on_the_equipment_they_belong_to()
    {
        var trb = Compose(UnitId.Trb);
        trb.ValveSlots.Count(v => v.IsHere).Should().Be(3);
        Compose(UnitId.Gen).ValvesNote.Should().Be("No valves on GEN-03.");
        Enumerable.Range(101, 9).Should().OnlyContain(id => ConsoleView.ValveOwner(id) != null);
    }
}
