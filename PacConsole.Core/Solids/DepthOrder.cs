using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>
/// A part's convex hull for ordering: a vertical prism over a convex footprint, from
/// <see cref="Y0"/> to <see cref="Y1"/>.
/// </summary>
public sealed record Volume(PlanPoint[] Footprint, float Y0, float Y1)
{
    public static Volume Box(Plan plan, float y, float height) =>
        new([new(plan.X, plan.Z), new(plan.X + plan.W, plan.Z), new(plan.X + plan.W, plan.Z + plan.D), new(plan.X, plan.Z + plan.D)], y, y + height);

    /// <summary>A circle's footprint as an octagon that contains it.</summary>
    public static Volume Round(float cx, float cz, float radius, float y, float height)
    {
        var r = radius / MathF.Cos(MathF.PI / 8);
        var ring = new PlanPoint[8];
        for (var i = 0; i < 8; i++)
        {
            var angle = (i + 0.5f) * MathF.PI / 4;
            ring[i] = new(cx + r * MathF.Cos(angle), cz + r * MathF.Sin(angle));
        }
        return new(ring, y, y + height);
    }

    public Volume Raised(float by) => by == 0 ? this : this with { Y0 = Y0 + by, Y1 = Y1 + by };

    public Vec3 Centre
    {
        get
        {
            float x = 0, z = 0;
            foreach (var p in Footprint) { x += p.X; z += p.Z; }
            return new(x / Footprint.Length, (Y0 + Y1) / 2, z / Footprint.Length);
        }
    }
}

/// <summary>
/// Painter's order for convex parts at any camera. Two parts whose screen boxes overlap are
/// ordered by a plane that separates them: a horizontal one when their heights do not overlap
/// (the camera looks down, so the upper part paints last), else a vertical plane through an edge
/// of either footprint (the part on the viewer's side paints last). For convex parts that do not
/// interpenetrate this is exact, so the order holds through a full orbit.
/// </summary>
public static class DepthOrder
{
    private const float Eps = 1e-3f;

    /// <summary>Indices of <paramref name="volumes"/>, back to front.</summary>
    public static int[] Sort(IReadOnlyList<Volume> volumes, OrbitProjection p)
    {
        var n = volumes.Count;
        var boxes = new (float L, float T, float R, float B)[n];
        var depth = new float[n];
        for (var i = 0; i < n; i++)
        {
            boxes[i] = ScreenBox(volumes[i], p);
            depth[i] = p.Depth(volumes[i].Centre);
        }

        var after = new List<int>[n];
        var waiting = new int[n];
        for (var i = 0; i < n; i++) after[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (!Overlap(boxes[i], boxes[j])) continue;
                var c = Compare(volumes[i], volumes[j], p, depth[i], depth[j]);
                if (c < 0) { after[i].Add(j); waiting[j]++; }
                else if (c > 0) { after[j].Add(i); waiting[i]++; }
            }
        }

        // Kahn's algorithm, farthest first among the ready parts; a cycle (only possible for
        // interpenetrating parts) is broken at the farthest part still waiting.
        var ready = new PriorityQueue<int, float>();
        for (var i = 0; i < n; i++) if (waiting[i] == 0) ready.Enqueue(i, depth[i]);
        var order = new int[n];
        var done = new bool[n];
        for (var k = 0; k < n; k++)
        {
            int next;
            if (ready.Count > 0)
            {
                next = ready.Dequeue();
            }
            else
            {
                next = -1;
                for (var i = 0; i < n; i++) if (!done[i] && (next < 0 || depth[i] < depth[next])) next = i;
            }
            done[next] = true;
            order[k] = next;
            foreach (var j in after[next])
            {
                if (--waiting[j] == 0 && !done[j]) ready.Enqueue(j, depth[j]);
            }
        }
        return order;
    }

    /// <summary>-1 when <paramref name="a"/> paints before <paramref name="b"/>, 1 after, 0 when either works.</summary>
    public static int Compare(Volume a, Volume b, OrbitProjection p, float depthA, float depthB)
    {
        if (a.Y0 >= b.Y1 - Eps) return 1;
        if (b.Y0 >= a.Y1 - Eps) return -1;

        float vx = p.SinYaw, vz = p.CosYaw;
        if (Separate(a.Footprint, a, b, vx, vz) is { } fromA) return fromA;
        if (Separate(b.Footprint, a, b, vx, vz) is { } fromB) return fromB;

        // Interpenetrating hulls: fall back to the centres.
        return depthA < depthB ? -1 : depthA > depthB ? 1 : 0;
    }

    private static int? Separate(PlanPoint[] edges, Volume a, Volume b, float vx, float vz)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            var e0 = edges[i];
            var e1 = edges[(i + 1) % edges.Length];
            float nx = e1.Z - e0.Z, nz = e0.X - e1.X;
            var length = MathF.Sqrt(nx * nx + nz * nz);
            if (length < 1e-6f) continue;
            nx /= length; nz /= length;
            var (minA, maxA) = Span(a.Footprint, nx, nz);
            var (minB, maxB) = Span(b.Footprint, nx, nz);
            var s = nx * vx + nz * vz;
            if (maxA <= minB + Eps) return MathF.Abs(s) <= Eps ? 0 : s > 0 ? -1 : 1;
            if (maxB <= minA + Eps) return MathF.Abs(s) <= Eps ? 0 : s > 0 ? 1 : -1;
        }
        return null;
    }

    private static (float Min, float Max) Span(PlanPoint[] points, float nx, float nz)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (var q in points)
        {
            var d = q.X * nx + q.Z * nz;
            if (d < min) min = d;
            if (d > max) max = d;
        }
        return (min, max);
    }

    private static (float L, float T, float R, float B) ScreenBox(Volume v, OrbitProjection p)
    {
        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        foreach (var q in v.Footprint)
        {
            foreach (var y in new[] { v.Y0, v.Y1 })
            {
                var s = p.Project(new(q.X, y, q.Z));
                l = MathF.Min(l, s.X); r = MathF.Max(r, s.X);
                t = MathF.Min(t, s.Y); b = MathF.Max(b, s.Y);
            }
        }
        return (l, t, r, b);
    }

    private static bool Overlap((float L, float T, float R, float B) a, (float L, float T, float R, float B) b) =>
        a.L < b.R && b.L < a.R && a.T < b.B && b.T < a.B;
}
