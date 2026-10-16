using PacConsole.Core.Operations;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class AlarmRulesTests
{
    private static bool Has(long t, string key, Setpoints? setpoints = null, PlantOps? ops = null) =>
        AlarmRules.At(t, setpoints ?? Setpoints.Default, ops ?? PlantOps.Initial).Any(a => a.Key == key && a.Raised);

    [Test]
    public void Cooling_bank_drifts_in_and_out_of_vibration_alarm()
    {
        Has(120, "clg.vib").Should().BeTrue("the console opens on the cooling bank in alarm");
        var states = Enumerable.Range(0, 1000).Select(t => Has(t, "clg.vib")).ToList();
        states.Should().Contain(true).And.Contain(false, "trends need alarm onsets to mark");
    }

    [Test]
    public void Only_the_cooling_bank_alarms_in_the_first_quarter_hour()
    {
        for (long t = 120; t < 120 + 450; t++)
        {
            AlarmRules.At(t, Setpoints.Default, PlantOps.Initial).Where(a => a.Raised).Select(a => a.Unit).Distinct().Should().BeSubsetOf([UnitId.Clg]);
        }
    }

    [Test]
    public void The_silo_fills_into_its_low_priority_alarm_and_unloading_clears_it()
    {
        Has(560, "sil.level").Should().BeFalse();
        Has(620, "sil.level").Should().BeTrue("78 % at tick 120 plus 0.015 % a tick passes 85 % near tick 587");
        AlarmRules.At(620, Setpoints.Default, PlantOps.Initial).Single(a => a.Key == "sil.level").Priority.Should().Be(AlarmPriority.Low);

        var unloading = PlantOps.Initial.Select(new StartDevice(UnitId.Sil, "sil.unload", "Unloading"), 620).Confirm(620, DateTimeOffset.UnixEpoch);
        Has(660, "sil.level", ops: unloading).Should().BeFalse("unloading takes 0.1 % a tick");
    }

    [Test]
    public void Turbine_alarms_when_its_limit_drops_below_its_pressure()
    {
        var press = Simulation.Readings(120).First(r => r.Id == UnitId.Trb).Press;
        Has(120, "trb.press", Setpoints.Default with { TurbineLimitBar = press + 0.01 }).Should().BeFalse();
        Has(120, "trb.press", Setpoints.Default with { TurbineLimitBar = press - 0.01 }).Should().BeTrue();
        AlarmRules.At(120, Setpoints.Default with { TurbineLimitBar = 150 }, PlantOps.Initial)
            .Single(a => a.Key == "trb.press").Priority.Should().Be(AlarmPriority.High);
    }

    [Test]
    public void Vibration_exactly_at_the_limit_is_not_an_alarm()
    {
        var readings = Simulation.Readings(120).Select(r => r.Id == UnitId.Gen ? r with { Vib = EquipmentCatalog.VibrationLimit } : r).ToImmutableArray();
        AlarmRules.Evaluate(120, readings, 600, Setpoints.Default, PlantOps.Initial).Should().NotContain(a => a.Key == "gen.vib" && a.Raised);
    }

    [Test]
    public void A_deadband_stops_an_alarm_chattering_at_its_limit()
    {
        // The cooling bank's vibration wanders ±0.2 mm/s about its drift: without a deadband the book
        // logged it in and out every few seconds.
        var book = AlarmBook.Compose(120 + 450, Setpoints.Default, PlantOps.Initial);
        var flips = book.Transitions.Count(t => t.Key == "clg.vib");
        flips.Should().BeLessThanOrEqualTo(4, "the slow drift crosses the limit twice per 10-minute cycle at most");
        foreach (var spec in EquipmentCatalog.All.SelectMany(e => e.Readings).Where(r => r.Alarm is not null))
        {
            spec.Alarm!.Deadband.Should().BeGreaterThan(0, spec.Key);
        }
    }

    [Test]
    public void Every_alarm_has_a_limit_a_title_and_guidance()
    {
        foreach (var spec in EquipmentCatalog.All.SelectMany(e => e.Readings).Where(r => r.Alarm is not null))
        {
            spec.Limit.Should().NotBeNull(spec.Key);
            spec.Alarm!.Title.Should().NotBeNullOrWhiteSpace(spec.Key);
            spec.Alarm.Guidance.Steps.Should().NotBeEmpty(spec.Key);
            spec.Alarm.Guidance.Consequence.Should().NotBeNullOrWhiteSpace(spec.Key);
        }
    }
}
