using PacConsole.Core.Console;
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
            Valve.Defaults,
            ControlMode.Auto,
            selected,
            T0,
            inputs: inputs);

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
    public void Alerts_map_to_the_reading_that_shows_them()
    {
        EquipmentCatalog.KeyFor(new Alert(UnitId.Clg, Metric.Vib, 5.1, 4.5)).Should().Be("clg.vib");
        EquipmentCatalog.KeyFor(new Alert(UnitId.Trb, Metric.Press, 170, 165)).Should().Be("trb.press");
        EquipmentCatalog.UnitOf("cnd.ttd").Should().Be(UnitId.Cnd);
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
    public void Every_system_keeps_the_stand_and_owns_some_of_the_drawing()
    {
        var parts = PacConsole.Core.Solids.PlantSolids.Default.All;
        foreach (var system in Enum.GetValues<PlantSystem>())
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
        view.ActiveAlertText.Should().Be("1 active alert");
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
