using Microsoft.Extensions.Time.Testing;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

public class TelemetryStreamTests
{
    [Test]
    public async Task Stream_yields_immediately_then_once_per_interval()
    {
        var time = new FakeTimeProvider();
        var telemetry = new SimulatedTelemetry(time);
        using var cts = new CancellationTokenSource();
        await using var e = telemetry.Stream(cts.Token).GetAsyncEnumerator(cts.Token);

        (await e.MoveNextAsync()).Should().BeTrue();
        e.Current.Tick.Should().Be(Simulation.StartTick);

        var next = e.MoveNextAsync();
        time.Advance(TimeSpan.FromSeconds(2));
        (await next).Should().BeTrue();
        e.Current.Tick.Should().Be(Simulation.StartTick + 1);
    }

    [Test]
    public async Task Paused_stream_does_not_advance()
    {
        var time = new FakeTimeProvider();
        var telemetry = new SimulatedTelemetry(time);
        using var cts = new CancellationTokenSource();
        await using var e = telemetry.Stream(cts.Token).GetAsyncEnumerator(cts.Token);
        await e.MoveNextAsync();

        telemetry.IsPaused = true;
        var next = e.MoveNextAsync();
        for (var i = 0; i < 5; i++)
        {
            time.Advance(TimeSpan.FromSeconds(2));
            await Task.Yield();
        }
        next.IsCompleted.Should().BeFalse();

        telemetry.IsPaused = false;
        time.Advance(TimeSpan.FromSeconds(2));
        (await next).Should().BeTrue();
        e.Current.Tick.Should().Be(Simulation.StartTick + 1);
    }
}
