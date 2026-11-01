using PacConsole.Core.Console;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

/// <summary>The rules the console model applies to the operator's input, kept in Core so they are tested here.</summary>
public class ModelRulesTests
{
    [TestCase(640.4, 640)]
    [TestCase(399, 400)]
    [TestCase(9000, 700)]
    [TestCase(-5, 400)]
    public void A_typed_load_is_rounded_and_kept_in_range(double draft, double applied)
    {
        SetpointRange.Load.Apply(draft).Should().Be(applied);
    }

    [Test]
    public void An_empty_field_applies_nothing()
    {
        // A NumberBox reports an empty field as NaN.
        SetpointRange.TurbineLimit.Apply(double.NaN).Should().BeNull();
        SetpointRange.Damper.Apply(101).Should().Be(100);
        SetpointRange.FanSpeed.Apply(10).Should().Be(60);
        SetpointRange.Reactive.Apply(250).Should().Be(200);
    }

    [Test]
    public void Comparing_starts_from_the_units_first_reading_and_keeps_three()
    {
        // Nothing picked: the first reading (shown by default) counts as picked.
        ImmutableArray<string> none = [];
        ViewInputs.ToggleCompared(none, "gen.frequency").Should().Equal("gen.gross", "gen.frequency");
        // Picking a shown reading again takes it out.
        ViewInputs.ToggleCompared(["gen.gross", "gen.frequency"], "gen.frequency").Should().Equal("gen.gross");
        // The oldest gives way past three.
        ViewInputs.ToggleCompared(["gen.gross", "gen.frequency", "gen.voltage"], "gen.stator").Should().Equal("gen.frequency", "gen.voltage", "gen.stator");
    }

    [Test]
    public void Comparing_follows_one_unit_and_ignores_unknown_keys()
    {
        // Another unit's reading starts that unit's list (TRB-04's first reading, inlet pressure, counts as picked).
        ViewInputs.ToggleCompared(["gen.gross", "gen.frequency"], "trb.temp").Should().Equal("trb.press", "trb.temp");
        ImmutableArray<string> picked = ["gen.gross"];
        ViewInputs.ToggleCompared(picked, "nope").Should().Equal(picked);
    }

    [Test]
    public void A_stopped_stream_shows_its_fault_an_ended_stream_or_no_telemetry()
    {
        var fault = new TelemetryFaultException("dropped");
        TelemetryFaultException.ForStop(fault, sentData: true).Should().BeSameAs(fault);
        TelemetryFaultException.ForStop(null, sentData: true).Should().BeOfType<TelemetryFaultException>()
            .Which.Message.Should().Contain("ended");
        TelemetryFaultException.ForStop(null, sentData: false).Should().BeNull("a stream that never sent a snapshot is the none state");
    }
}
