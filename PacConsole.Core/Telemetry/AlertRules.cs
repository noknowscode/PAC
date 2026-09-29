using PacConsole.Core.Plant;

namespace PacConsole.Core.Telemetry;

/// <summary>The two alert rules the console knows. Pure, so the boundaries are unit-tested.</summary>
public static class AlertRules
{
    /// <summary>Vibration above this, in mm/s, is an alert on any unit (ISO 10816 zone C).</summary>
    public const double VibrationLimit = 4.5;

    public static ImmutableArray<Alert> Evaluate(IEnumerable<UnitReading> readings, Setpoints setpoints)
    {
        var alerts = ImmutableArray.CreateBuilder<Alert>();
        foreach (var r in readings)
        {
            if (r.Id == UnitId.Trb && r.Press > setpoints.TurbineLimitBar)
            {
                alerts.Add(new Alert(r.Id, Metric.Press, r.Press, setpoints.TurbineLimitBar));
            }
            if (r.Vib > VibrationLimit)
            {
                alerts.Add(new Alert(r.Id, Metric.Vib, r.Vib, VibrationLimit));
            }
        }
        return alerts.ToImmutable();
    }

    /// <summary>Units with at least one alert, in display order.</summary>
    public static ImmutableArray<UnitId> AlertingUnits(ImmutableArray<Alert> alerts) =>
        [.. alerts.Select(a => a.Unit).Distinct().OrderBy(u => u)];
}
