using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class AlertRulesTests
{
    [Test]
    public void Cooling_bank_drifts_in_and_out_of_vibration_alarm()
    {
        bool Alerting(long t) => AlertRules.Evaluate(Simulation.Readings(t), Setpoints.Default)
            .Any(a => a.Unit == UnitId.Clg && a.Metric == Metric.Vib);

        Alerting(120).Should().BeTrue("the console opens on the cooling bank in alarm");
        var states = Enumerable.Range(0, 1000).Select(t => Alerting(t)).ToList();
        states.Should().Contain(true).And.Contain(false, "trends need alert onsets to mark");
    }

    [Test]
    public void Only_the_cooling_bank_alerts_at_default_setpoints()
    {
        for (long t = 0; t < 1000; t++)
        {
            var alerts = AlertRules.Evaluate(Simulation.Readings(t), Setpoints.Default);
            AlertRules.AlertingUnits(alerts).Should().BeSubsetOf([UnitId.Clg]);
        }
    }

    [Test]
    public void Turbine_alerts_when_limit_drops_below_its_pressure()
    {
        var readings = Simulation.Readings(120);
        var trbPress = readings.First(r => r.Id == UnitId.Trb).Press;

        AlertRules.Evaluate(readings, Setpoints.Default with { TurbineLimitBar = trbPress + 0.01 })
            .Should().NotContain(a => a.Unit == UnitId.Trb);
        AlertRules.Evaluate(readings, Setpoints.Default with { TurbineLimitBar = trbPress - 0.01 })
            .Should().ContainSingle(a => a.Unit == UnitId.Trb && a.Metric == Metric.Press);
    }

    [Test]
    public void Vibration_exactly_at_limit_is_not_an_alert()
    {
        var reading = new UnitReading(UnitId.Gen, 0, 0, 0, 0, 0, AlertRules.VibrationLimit);
        AlertRules.Evaluate([reading], Setpoints.Default).Should().BeEmpty();
    }
}
