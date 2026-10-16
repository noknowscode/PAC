using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class SimulationTests
{
    [Test]
    public void Same_tick_gives_identical_readings()
    {
        Simulation.Readings(120).Should().Equal(Simulation.Readings(120));
        Simulation.OutputHistory(120).Should().Equal(Simulation.OutputHistory(120));
    }

    [Test]
    public void Noise_stays_in_unit_interval()
    {
        for (var i = -500; i < 500; i++)
        {
            Simulation.Noise(i, 3).Should().BeInRange(0, 1);
        }
    }

    [Test]
    public void Readings_cover_all_six_units_in_display_order()
    {
        Simulation.Readings(120).Select(r => r.Id).Should().Equal(Enum.GetValues<UnitId>());
    }

    [Test]
    public void Damper_scales_only_stack_flow()
    {
        var reference = Simulation.Readings(120, 65);
        var half = Simulation.Readings(120, 32.5);

        half[0].Flow.Should().BeApproximately(reference[0].Flow / 2, 1e-9);
        half.Skip(1).Should().Equal(reference.Skip(1));
    }

    [Test]
    public void Each_open_valve_beyond_seven_adds_six_mw()
    {
        (Simulation.OutputMw(120, 9) - Simulation.OutputMw(120, 7)).Should().BeApproximately(12, 1e-9);
    }

    [Test]
    public void Series_have_spec_lengths_and_end_at_current_tick()
    {
        Simulation.OutputHistory(200).Should().HaveCount(48).And.EndWith(Simulation.OutputMw(200));
        Simulation.HourlyGeneration(200).Should().HaveCount(24);
        Simulation.VibrationHistory(UnitId.Clg, 200).Should().HaveCount(30).And.EndWith(Simulation.Vibration(UnitId.Clg, 200));
    }

    [Test]
    public void Output_stays_inside_chart_range_for_a_day_of_ticks()
    {
        for (long t = 0; t < 2000; t++)
        {
            Simulation.OutputMw(t).Should().BeInRange(480, 700);
        }
    }
}
