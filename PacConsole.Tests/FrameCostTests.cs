using System.Diagnostics;
using PacConsole.Core.Geometry;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>
/// The CPU work behind one viewport frame, without Skia: order the 139 parts, build every shape and
/// its line work. SPEC budget for the whole frame is 4 ms; this measures the Core share of it.
/// </summary>
public class FrameCostTests
{
    [Test, Explicit("Timing: run on demand")]
    public void Core_work_per_frame_fits_the_budget()
    {
        var parts = PlantSolids.Default.All;
        var volumes = parts.Select(p => p.Volume).ToList();
        void Frame(int i)
        {
            var p = PlantSolids.ViewProjection(new CameraPose(i * 0.01f, 0.49f), 1240, 760);
            var order = DepthOrder.Sort(volumes, p);
            foreach (var k in order)
            {
                _ = parts[k].Build(p);
                if (parts[k].Details is { } d) _ = d(p, i / 60.0).ToList();
            }
        }
        // Warm up past tiered JIT: the first ~100 frames run unoptimised code and read 3x slower.
        for (var i = 0; i < 300; i++) Frame(i);
        var sw = Stopwatch.StartNew();
        const int frames = 120;
        for (var i = 0; i < frames; i++) Frame(i);
        var ms = sw.Elapsed.TotalMilliseconds / frames;
        TestContext.Out.WriteLine($"Core work per frame: {ms:0.00} ms");
        ms.Should().BeLessThan(4);
    }

    [Test, Explicit("Timing: run on demand")]
    public void Composing_the_console_view_fits_the_budget()
    {
        // The view recomposes every snapshot (2 s) and on every operator input; the alarm book replays 15 min.
        var t0 = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        var ops = PacConsole.Core.Operations.PlantOps.Initial;
        PacConsole.Core.Console.ConsoleView Compose(long tick) => PacConsole.Core.Console.ConsoleView.Compose(
            new PacConsole.Core.Telemetry.PlantSnapshot(tick, t0, PacConsole.Core.Telemetry.Simulation.Readings(tick)),
            PacConsole.Core.Telemetry.Setpoints.Default, ops, PacConsole.Core.Plant.ControlMode.Auto, null, t0);
        for (var i = 0; i < 20; i++) Compose(600 + i);
        var sw = Stopwatch.StartNew();
        const int n = 20;
        for (var i = 0; i < n; i++) Compose(700 + i);
        var ms = sw.Elapsed.TotalMilliseconds / n;
        TestContext.Out.WriteLine($"ConsoleView.Compose: {ms:0.00} ms");
        ms.Should().BeLessThan(5);
    }

    [Test, Explicit("Timing: run on demand")]
    public void Where_the_compose_time_goes()
    {
        var ops = PacConsole.Core.Operations.PlantOps.Initial;
        var sp = PacConsole.Core.Telemetry.Setpoints.Default;
        var t0 = new DateTimeOffset(2026, 10, 16, 2, 0, 0, TimeSpan.Zero);
        double Time(Action<int> a)
        {
            for (var i = 0; i < 10; i++) a(i);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) a(i);
            return sw.Elapsed.TotalMilliseconds / 20;
        }
        TestContext.Out.WriteLine($"AlarmBook: {Time(i => PacConsole.Core.Operations.AlarmBook.Compose(700 + i, sp, ops)):0.00} ms");
        TestContext.Out.WriteLine($"AlarmRules.At x450: {Time(i => { for (var t = 0; t < 450; t++) PacConsole.Core.Operations.AlarmRules.At(700 + i + t, sp, ops); }):0.00} ms");
        TestContext.Out.WriteLine($"Readings x450: {Time(i => { for (var t = 0; t < 450; t++) PacConsole.Core.Telemetry.Simulation.Readings(700 + i + t, 65, ops); }):0.00} ms");
        TestContext.Out.WriteLine($"OutputMw x450: {Time(i => { for (var t = 0; t < 450; t++) PacConsole.Core.Telemetry.Simulation.OutputMw(700 + i + t, 7, ops); }):0.00} ms");
        TestContext.Out.WriteLine($"Trends 15m: {Time(i => PacConsole.Core.Console.TrendSet.Compose(700 + i, t0, sp, ops, PacConsole.Core.Console.TrendRange.Minutes15, ["clg.basin"], -1)):0.00} ms");
        TestContext.Out.WriteLine($"Trends 8h: {Time(i => PacConsole.Core.Console.TrendSet.Compose(700 + i, t0, sp, ops, PacConsole.Core.Console.TrendRange.Hours8, ["clg.basin"], -1)):0.00} ms");
    }

    [Test, Explicit("Timing: run on demand")]
    public void Where_the_frame_time_goes()
    {
        var parts = PlantSolids.Default.All;
        var volumes = parts.Select(p => p.Volume).ToList();
        double sort = 0, build = 0, details = 0;
        var byName = new Dictionary<string, double>();
        for (var i = 0; i < 140; i++)
        {
            var p = PlantSolids.ViewProjection(new CameraPose(i * 0.01f, 0.49f), 1240, 760);
            var sw = Stopwatch.StartNew();
            var order = DepthOrder.Sort(volumes, p);
            if (i >= 20) sort += sw.Elapsed.TotalMilliseconds;
            foreach (var k in order)
            {
                sw.Restart();
                _ = parts[k].Build(p);
                var b = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                if (parts[k].Details is { } d) _ = d(p, i / 60.0).ToList();
                var dt = sw.Elapsed.TotalMilliseconds;
                if (i < 20) continue;
                build += b; details += dt;
                byName[parts[k].Name] = byName.GetValueOrDefault(parts[k].Name) + b;
            }
        }
        TestContext.Out.WriteLine($"sort {sort / 120:0.00} ms, build {build / 120:0.00} ms, details {details / 120:0.00} ms");
        foreach (var (n, t) in byName.OrderByDescending(kv => kv.Value).Take(8)) TestContext.Out.WriteLine($"  {n}: {t / 120:0.000} ms");
    }
}
