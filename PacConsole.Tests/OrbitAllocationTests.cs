using PacConsole.Core.Geometry;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>
/// An orbit frame builds every part at a new camera. Its scratch work (hull sorts, lathe sample rings, the depth sort's
/// pairs and queues) reuses buffers, so the frame allocates little beyond the shapes it hands the renderer, and the
/// hull is the same one the kit's sort gave.
/// </summary>
public class OrbitAllocationTests
{
    private static readonly ImmutableArray<SolidPart> Parts = PlantSolids.Default.All;

    /// <summary>The kit's hull, as it was written before the buffers: a LINQ sort and a growing list.</summary>
    private static Vec2[] ReferenceHull(IReadOnlyCollection<Vec2> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3) return sorted;
        static float Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Vec2>();
        foreach (var point in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        var lower = hull.Count;
        for (var i = sorted.Length - 2; i >= 0; i--)
        {
            while (hull.Count > lower && Cross(hull[^2], hull[^1], sorted[i]) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(sorted[i]);
        }
        hull.RemoveAt(hull.Count - 1);
        return [.. hull];
    }

    [Test]
    public void The_buffered_hull_is_the_kit_s_hull_including_duplicates_and_collinear_points()
    {
        var random = new Random(7);
        for (var trial = 0; trial < 500; trial++)
        {
            var count = random.Next(0, 60);
            // Coarse coordinates, so duplicates and collinear runs are common.
            var points = Enumerable.Range(0, count).Select(_ => new Vec2(random.Next(0, 12), random.Next(0, 12))).ToList();

            SolidBuilder.ConvexHull(points).Should().Equal(ReferenceHull(points), $"trial {trial}");
        }
    }

    [Test]
    public void An_orbit_frame_s_geometry_allocates_about_half_what_it_did()
    {
        var cache = new DepthCache();
        long Frame(int i)
        {
            var pose = new CameraPose(CameraPose.Iso.Yaw + i * 0.002f, CameraPose.Iso.Elevation);
            var p = PlantSolids.ViewProjection(pose, 1084, 620);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var volumes = new List<Volume>(Parts.Length);
            foreach (var part in Parts)
            {
                part.Build(p);
                volumes.Add(part.Volume);
            }
            DepthOrder.Sort(volumes, p, cache);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        for (var i = 0; i < 3; i++) Frame(i);

        var bytes = Enumerable.Range(3, 5).Select(Frame).Min();

        TestContext.Out.WriteLine($"Orbit frame geometry: {bytes / 1024.0:0} KB");
        // Before the buffers (uno-audit, 2026-11-04): about 2.2 MB building the parts and 0.44 MB sorting them. After:
        // 1.2 MB, nearly all of it the shapes the renderer keeps for the frame.
        bytes.Should().BeLessThan(1_400_000);
    }
}
