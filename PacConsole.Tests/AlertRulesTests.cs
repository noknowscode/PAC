using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class AlertRulesTests
{
    [Test]
    public void Condenser_is_always_alerting_on_vibration()
    {
        for (long t = 0; t < 1000; t++)
        {
            var alerts = AlertRules.Evaluate(Simulation.Readings(t), Setpoints.Default);
            alerts.Should().Contain(a => a.Unit == UnitId.Cnd && a.Metric == Metric.Vib);
        }
    }

    [Test]
    public void Only_the_condenser_alerts_at_default_setpoints()
    {
        for (long t = 0; t < 1000; t++)
        {
            var alerts = AlertRules.Evaluate(Simulation.Readings(t), Setpoints.Default);
            AlertRules.AlertingUnits(alerts).Should().Equal(UnitId.Cnd);
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
