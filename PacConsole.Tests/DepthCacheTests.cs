using System.Diagnostics;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>A cached depth sort must give exactly the order a full sort gives, through lifts and camera changes.</summary>
public class DepthCacheTests
{
    private static readonly ImmutableArray<SolidPart> Parts = PlantSolids.Default.All;

    private static List<Volume> Lifted(UnitId? up, float upBy, UnitId? down, float downBy) =>
        [.. Parts.Select(p => p.Volume.Raised(p.Unit == up ? upBy : p.Unit == down ? downBy : 0))];

    [Test]
    public void Cached_sort_matches_the_full_sort_through_selection_lifts()
    {
        var cache = new DepthCache();
        var units = UnitCatalog.All.Select(u => u.Id).ToArray();
        foreach (var pose in new[] { CameraPose.Iso, new CameraPose(2.1f, 0.35f), new CameraPose(-0.7f, 1.2f) })
        {
            var p = PlantSolids.ViewProjection(pose, 1084, 620);
            for (var u = 0; u < units.Length; u++)
            {
                var previous = units[(u + units.Length - 1) % units.Length];
                foreach (var t in new[] { 0f, 0.1f, 0.35f, 0.7f, 1f })
                {
                    var volumes = Lifted(units[u], t * SolidLift, previous, (1 - t) * SolidLift);
                    DepthOrder.Sort(volumes, p, cache).Should().Equal(DepthOrder.Sort(volumes, p));
                }
            }
        }
    }

    private const float SolidLift = 10;

    [Test, Explicit("Timing: run on demand")]
    public void A_lift_frame_sorts_faster_with_the_cache()
    {
        var p = PlantSolids.ViewProjection(CameraPose.Iso, 1084, 620);
        var cache = new DepthCache();
        var frames = Enumerable.Range(0, 60).Select(i => Lifted(UnitId.Clg, i / 60f * SolidLift, UnitId.Trb, (1 - i / 60f) * SolidLift)).ToList();
        double Time(Func<List<Volume>, int[]> sort)
        {
            for (var w = 0; w < 3; w++) foreach (var f in frames) sort(f);
            var sw = Stopwatch.StartNew();
            foreach (var f in frames) sort(f);
            return sw.Elapsed.TotalMilliseconds / frames.Count;
        }
        var full = Time(v => DepthOrder.Sort(v, p));
        var cached = Time(v => DepthOrder.Sort(v, p, cache));
        TestContext.Out.WriteLine($"Sort per lift frame: full {full:0.00} ms, cached {cached:0.00} ms");
        cached.Should().BeLessThan(full);
    }
}
