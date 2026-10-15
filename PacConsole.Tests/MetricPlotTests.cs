using PacConsole.Core.Console;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class MetricPlotTests
{
    private const long Tick = Simulation.StartTick;

    [TestCase(UnitId.Cnd, Metric.Vib, PlotKind.Zones)]
    [TestCase(UnitId.Trb, Metric.Press, PlotKind.Limit)]
    [TestCase(UnitId.Gen, Metric.Press, PlotKind.Band)]
    [TestCase(UnitId.Gen, Metric.Load, PlotKind.Share)]
    [TestCase(UnitId.Stk, Metric.Eff, PlotKind.Design)]
    [TestCase(UnitId.Tka, Metric.Temp, PlotKind.Band)]
    [TestCase(UnitId.Stk, Metric.Flow, PlotKind.Band)]
    public void Each_reading_gets_the_presentation_that_suits_it(UnitId unit, Metric metric, PlotKind kind)
    {
        MetricPlot.Compose(unit, metric, Tick, Setpoints.Default, false).Kind.Should().Be(kind);
    }

    [Test]
    public void The_turbine_pressure_limit_follows_the_setpoint()
    {
        var plot = MetricPlot.Compose(UnitId.Trb, Metric.Press, Tick, Setpoints.Default with { TurbineLimitBar = 152 }, false);
        plot.Marks.Should().ContainSingle().Which.Should().Be(new PlotMark(152, 152, "limit 152"));
    }

    [Test]
    public void Stack_flow_band_scales_with_the_damper()
    {
        var at65 = MetricPlot.Compose(UnitId.Stk, Metric.Flow, Tick, Setpoints.Default with { DamperPct = 65 }, false);
        var at30 = MetricPlot.Compose(UnitId.Stk, Metric.Flow, Tick, Setpoints.Default with { DamperPct = 30 }, false);
        at30.Marks[0].High.Should().BeLessThan(at65.Marks[0].Low);
        at30.Note.Should().Contain("30 %");
    }

    [Test]
    public void The_range_holds_the_series_and_its_references_in_two_or_three_steps()
    {
        foreach (var unit in Enum.GetValues<UnitId>())
        {
            foreach (var metric in Enum.GetValues<Metric>())
            {
                var plot = MetricPlot.Compose(unit, metric, Tick, Setpoints.Default, false);
                var name = $"{unit} {metric}";
                plot.Series.Should().HaveCount(Simulation.VibrationHistoryLength, name);
                plot.Series.Should().OnlyContain(v => v >= plot.Min && v <= plot.Max, name);
                foreach (var mark in plot.Marks.Where(m => plot.Kind != PlotKind.Zones))
                {
                    mark.Low.Should().BeInRange(plot.Min, plot.Max, name);
                    mark.High.Should().BeInRange(plot.Min, plot.Max, name);
                }
                ((plot.Max - plot.Min) / plot.Step).Should().BeInRange(2 - 1e-9, 4 + 1e-9, name);
            }
        }
    }

    [Test]
    public void The_newest_sample_is_the_live_reading()
    {
        var reading = Simulation.Readings(Tick).First(r => r.Id == UnitId.Gen);
        MetricPlot.Compose(UnitId.Gen, Metric.Temp, Tick, Setpoints.Default, false).Series[^1].Should().Be(reading.Temp);
    }

    [Test]
    public void The_view_plots_the_focused_reading_and_flags_its_tile()
    {
        var view = ConsoleView.Compose(
            new PlantSnapshot(Tick, DateTimeOffset.UnixEpoch, Simulation.Readings(Tick)),
            Setpoints.Default, Valve.Defaults, ControlMode.Auto, UnitId.Trb, DateTimeOffset.UnixEpoch, Metric.Press);
        view.Plot!.Kind.Should().Be(PlotKind.Limit);
        view.Tiles.Single(t => t.IsFocused).Metric.Should().Be(Metric.Press);
        view.OpenValvesText.Should().Be("7 of 9 open");
        view.OutputDelta.Should().EndWith("MW vs target 620 MW");
    }
}
