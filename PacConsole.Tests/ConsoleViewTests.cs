using PacConsole.Core.Console;
using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class ConsoleViewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 16, 0, 0, TimeSpan.Zero);

    private static ConsoleView Compose(
        ControlMode mode = ControlMode.Auto,
        UnitId? selected = null,
        Setpoints? setpoints = null,
        TimeSpan? age = null) =>
        ConsoleView.Compose(
            new PlantSnapshot(Simulation.StartTick, T0, Simulation.Readings(Simulation.StartTick)),
            PlantOps.Initial with { Setpoints = setpoints ?? Setpoints.Default },
            mode,
            selected,
            T0 + (age ?? TimeSpan.Zero));

    [Test]
    public void Opens_on_the_alerting_cooling_bank()
    {
        var view = Compose();
        view.Selected.Should().Be(UnitId.Clg);
        view.SelectedIsAlert.Should().BeTrue();
        view.ActiveAlertText.Should().Be("1 active alarm");
    }

    [Test]
    public void Explicit_selection_wins_over_the_alert_default()
    {
        Compose(selected: UnitId.Trb).Selected.Should().Be(UnitId.Trb);
    }

    [Test]
    public void Hold_ages_the_data_and_goes_stale_at_ten_seconds()
    {
        var fresh = Compose(ControlMode.Hold, age: TimeSpan.FromSeconds(9));
        fresh.DataIsStale.Should().BeFalse();
        fresh.ModeLine.Should().Be("Data held 9 s");
        var held = Compose(ControlMode.Hold, age: TimeSpan.FromSeconds(12));
        held.DataIsStale.Should().BeTrue();
        held.DataIsLive.Should().BeFalse();
        held.ModeLine.Should().Be("Data held 12 s · stale");
    }

    [Test]
    public void Held_data_keeps_the_snapshots_times_everywhere()
    {
        // Held for 30 s: the wall clock moves on, the snapshot does not, and neither do the times shown.
        var held = Compose(ControlMode.Hold, age: TimeSpan.FromSeconds(30));
        held.Trends!.Times[^1].Should().Be(T0, "the newest sample is the snapshot");
        held.CursorTime.Should().Be(T0.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        held.SnapshotText.Should().EndWith(held.CursorTime);
        held.Trends.Times.Should().Equal(Compose().Trends!.Times, "trends while held are the trends at the snapshot");
    }

    [Test]
    public void Low_turbine_limit_raises_a_second_alert()
    {
        var view = Compose(setpoints: Setpoints.Default with { TurbineLimitBar = 150 }, selected: UnitId.Trb);
        view.AlertingUnits.Should().BeEquivalentTo([UnitId.Trb, UnitId.Clg]);
        view.ActiveAlertText.Should().Be("2 active alarms");
        view.OverviewTiles.Single(t => t.Key == "trb.press").IsHigh.Should().BeTrue();
    }
}
