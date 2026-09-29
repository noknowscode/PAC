using PacConsole.Core.Console;
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
            setpoints ?? Setpoints.Default,
            Valve.Defaults,
            mode,
            selected,
            T0 + (age ?? TimeSpan.Zero));

    [Test]
    public void Opens_on_the_alerting_condenser()
    {
        var view = Compose();
        view.Selected.Should().Be(UnitId.Cnd);
        view.SelectedIsAlert.Should().BeTrue();
        view.AlertText.Should().Be("1 ACTIVE ALERT · CND-05");
    }

    [Test]
    public void Explicit_selection_wins_over_the_alert_default()
    {
        Compose(selected: UnitId.Trb).Selected.Should().Be(UnitId.Trb);
    }

    [Test]
    public void Hold_ages_the_data_and_goes_stale_at_ten_seconds()
    {
        Compose(ControlMode.Hold, age: TimeSpan.FromSeconds(9)).DataIsStale.Should().BeFalse();
        var held = Compose(ControlMode.Hold, age: TimeSpan.FromSeconds(12));
        held.DataText.Should().Be("DATA HELD 12s");
        held.DataIsStale.Should().BeTrue();
        held.SelectedStatusLine.Should().Be("ALERT · HELD");
    }

    [Test]
    public void Held_tiles_dim_except_alert_values()
    {
        var tiles = Compose(ControlMode.Hold).Tiles;
        tiles.Single(t => t.Label == "VIB").Should().Match<MetricTileView>(t => t.IsHigh && !t.IsHeld);
        tiles.Where(t => t.Label != "VIB").Should().OnlyContain(t => t.IsHeld);
    }

    [Test]
    public void Low_turbine_limit_raises_a_second_alert()
    {
        var view = Compose(setpoints: Setpoints.Default with { TurbineLimitBar = 150 }, selected: UnitId.Trb);
        view.AlertText.Should().Be("2 ACTIVE ALERTS · TRB-04, CND-05");
        view.Tiles.Single(t => t.Label == "PRESS").IsHigh.Should().BeTrue();
    }

    [Test]
    public void Unit_tiles_mark_selection_and_alerts()
    {
        var units = Compose().Units;
        units.Should().HaveCount(6);
        units.Single(u => u.IsSelected).Id.Should().Be(UnitId.Cnd);
        units.Single(u => u.Id == UnitId.Cnd).Subtitle.Should().Be("05 · ALERT");
        units.Single(u => u.Id == UnitId.Stk).Subtitle.Should().Be("01 · OK");
    }

    [TestCase(Metric.Press, 0.0912, "0.09")]
    [TestCase(Metric.Press, 6.43, "6.4")]
    [TestCase(Metric.Press, 165.2, "165")]
    [TestCase(Metric.Flow, 1540.4, "1,540")]
    [TestCase(Metric.Vib, 4.849, "4.8")]
    public void Metrics_format_like_the_prototype(Metric metric, double value, string expected)
    {
        ConsoleView.FormatMetric(metric, value).Should().Be(expected);
    }
}
