using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>
/// A part's convex hull for ordering: a vertical prism over a convex footprint, from
/// <see cref="Y0"/> to <see cref="Y1"/>, optionally tapered (a cone's hull is narrower at one
/// end). <see cref="Footprint"/> is the widest plan extent and is what plan separation uses.
/// </summary>
public sealed record Volume(PlanPoint[] Footprint, float Y0, float Y1)
{
    /// <summary>The plan at <see cref="Y0"/>; the footprint unless tapered.</summary>
    public PlanPoint[] Bottom { get; init; } = Footprint;

    /// <summary>The plan at <see cref="Y1"/>; the footprint unless tapered.</summary>
    public PlanPoint[] Top { get; init; } = Footprint;

    public static Volume Box(Plan plan, float y, float height) =>
        new([new(plan.X, plan.Z), new(plan.X + plan.W, plan.Z), new(plan.X + plan.W, plan.Z + plan.D), new(plan.X, plan.Z + plan.D)], y, y + height);

    /// <summary>A circle's footprint as an octagon that contains it.</summary>
    public static Volume Round(float cx, float cz, float radius, float y, float height) =>
        new(Octagon(cx, cz, radius), y, y + height);

    /// <summary>A cone or frustum from radius <paramref name="r0"/> at <paramref name="y"/> to <paramref name="r1"/> at the top.</summary>
    public static Volume Cone(float cx, float cz, float r0, float r1, float y, float height) =>
        new(Octagon(cx, cz, MathF.Max(r0, r1)), y, y + height) { Bottom = Octagon(cx, cz, r0), Top = Octagon(cx, cz, r1) };

    private static PlanPoint[] Octagon(float cx, float cz, float radius)
    {
        var r = radius / MathF.Cos(MathF.PI / 8);
        var ring = new PlanPoint[8];
        for (var i = 0; i < 8; i++)
        {
            var angle = (i + 0.5f) * MathF.PI / 4;
            ring[i] = new(cx + r * MathF.Cos(angle), cz + r * MathF.Sin(angle));
        }
        return ring;
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

    /// <summary>
    /// Indices of <paramref name="volumes"/>, back to front. With a <paramref name="cache"/>, only the
    /// parts whose volume changed since the last sort at the same camera (a unit lifting off its pad)
    /// have their silhouette and pairs worked out again; the order is the same as without it.
    /// </summary>
    public static int[] Sort(IReadOnlyList<Volume> volumes, OrbitProjection p, DepthCache? cache = null)
    {
        var n = volumes.Count;
        float[] depth;
        List<int>[] after;
        if (cache is null)
        {
            depth = new float[n];
            for (var i = 0; i < n; i++) depth[i] = p.Depth(volumes[i].Centre);
            after = Constraints(volumes, p, depth);
        }
        else
        {
            cache.Update(volumes, p);
            depth = cache.Depth;
            after = cache.Constraints(volumes, p);
        }
        var waiting = new int[n];
        foreach (var list in after) foreach (var j in list) waiting[j]++;

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

    /// <summary>
    /// For each part, the parts that must paint after it: one entry per pair whose silhouettes
    /// truly overlap. Two parts whose boxes overlap but silhouettes do not can hold contradictory
    /// "valid" planes, which closes a cycle (TRB's lowest step, below and in front of the deck).
    /// </summary>
    public static List<int>[] Constraints(IReadOnlyList<Volume> volumes, OrbitProjection p, float[]? depth = null)
    {
        var n = volumes.Count;
        var hulls = new Vec2[n][];
        var boxes = new (float L, float T, float R, float B)[n];
        for (var i = 0; i < n; i++)
        {
            hulls[i] = ScreenHull(volumes[i], p);
            boxes[i] = Box(hulls[i]);
        }
        depth ??= [.. volumes.Select(v => p.Depth(v.Centre))];

        var after = new List<int>[n];
        for (var i = 0; i < n; i++) after[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                if (!Overlap(boxes[i], boxes[j]) || !Overlap(hulls[i], hulls[j])) continue;
                var c = Compare(volumes[i], volumes[j], p, depth[i], depth[j]);
                if (c < 0) after[i].Add(j);
                else if (c > 0) after[j].Add(i);
            }
        }
        return after;
    }

    /// <summary>The pair's entry in <see cref="Constraints"/>: 2 when silhouettes do not overlap, else <see cref="Compare"/>.</summary>
    internal static sbyte Pair(Volume a, Volume b, Vec2[] hullA, Vec2[] hullB, (float L, float T, float R, float B) boxA, (float L, float T, float R, float B) boxB, OrbitProjection p, float depthA, float depthB) =>
        !Overlap(boxA, boxB) || !Overlap(hullA, hullB) ? (sbyte)2 : (sbyte)Compare(a, b, p, depthA, depthB);

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

    /// <summary>
    /// True when a plane separates the two hulls (touching counts). When every pair is separable,
    /// <see cref="Compare"/> never falls back to centres and the order is exact at any camera.
    /// </summary>
    public static bool Separable(Volume a, Volume b)
    {
        if (a.Y0 >= b.Y1 - Eps || b.Y0 >= a.Y1 - Eps) return true;
        return SeparatedOnPlan(a.Footprint, a, b) || SeparatedOnPlan(b.Footprint, a, b);
    }

    private static bool SeparatedOnPlan(PlanPoint[] edges, Volume a, Volume b)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            var e0 = edges[i];
            var e1 = edges[(i + 1) % edges.Length];
            float nx = e1.Z - e0.Z, nz = e0.X - e1.X;
            var length = MathF.Sqrt(nx * nx + nz * nz);
            if (length < 1e-6f) continue;
            var (minA, maxA) = Span(a.Footprint, nx / length, nz / length);
            var (minB, maxB) = Span(b.Footprint, nx / length, nz / length);
            if (maxA <= minB + Eps || maxB <= minA + Eps) return true;
        }
        return false;
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

    /// <summary>The hull's silhouette: the convex hull of its corners on screen.</summary>
    internal static Vec2[] ScreenHull(Volume v, OrbitProjection p)
    {
        var corners = new Vec2[v.Bottom.Length + v.Top.Length];
        for (var i = 0; i < v.Bottom.Length; i++) corners[i] = p.Project(new(v.Bottom[i].X, v.Y0, v.Bottom[i].Z));
        for (var i = 0; i < v.Top.Length; i++) corners[v.Bottom.Length + i] = p.Project(new(v.Top[i].X, v.Y1, v.Top[i].Z));
        return SolidBuilder.ConvexHull(corners);
    }

    internal static (float L, float T, float R, float B) Box(Vec2[] hull)
    {
        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        foreach (var s in hull)
        {
            l = MathF.Min(l, s.X); r = MathF.Max(r, s.X);
            t = MathF.Min(t, s.Y); b = MathF.Max(b, s.Y);
        }
        return (l, t, r, b);
    }

    /// <summary>Convex polygons overlap by more than a touch (2D separating axes).</summary>
    private static bool Overlap(Vec2[] a, Vec2[] b) => !SeparatedOnScreen(a, a, b) && !SeparatedOnScreen(b, a, b);

    private static bool SeparatedOnScreen(Vec2[] edges, Vec2[] a, Vec2[] b)
    {
        for (var i = 0; i < edges.Length; i++)
        {
            var e0 = edges[i];
            var e1 = edges[(i + 1) % edges.Length];
            float nx = e1.Y - e0.Y, ny = e0.X - e1.X;
            var length = MathF.Sqrt(nx * nx + ny * ny);
            if (length < 1e-6f) continue;
            nx /= length; ny /= length;
            float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
            foreach (var q in a) { var d = q.X * nx + q.Y * ny; minA = MathF.Min(minA, d); maxA = MathF.Max(maxA, d); }
            foreach (var q in b) { var d = q.X * nx + q.Y * ny; minB = MathF.Min(minB, d); maxB = MathF.Max(maxB, d); }
            if (maxA <= minB + Eps || maxB <= minA + Eps) return true;
        }
        return false;
    }

    private static bool Overlap((float L, float T, float R, float B) a, (float L, float T, float R, float B) b) =>
        a.L < b.R && b.L < a.R && a.T < b.B && b.T < a.B;
}

/// <summary>
/// What <see cref="DepthOrder.Sort"/> worked out at one camera: each part's silhouette and depth, and
/// the order of every pair. A lift moves one unit's parts, so a lift frame redoes the pairs that
/// involve them (a few thousand of ~44,000) instead of all of them. A new camera starts over.
/// </summary>
public sealed class DepthCache
{
    private const sbyte Unknown = sbyte.MinValue;

    private (float, float, float, float, float, float, float) _camera;
    private Volume?[] _volumes = [];
    private Vec2[][] _hulls = [];
    private (float L, float T, float R, float B)[] _boxes = [];
    private sbyte[] _pairs = [];

    internal float[] Depth { get; private set; } = [];

    internal void Update(IReadOnlyList<Volume> volumes, OrbitProjection p)
    {
        var n = volumes.Count;
        var camera = (p.SinYaw, p.CosYaw, p.SinElevation, p.CosElevation, p.Scale, p.CenterX, p.CenterY);
        if (camera != _camera || n != _volumes.Length)
        {
            _camera = camera;
            _volumes = new Volume?[n];
            _hulls = new Vec2[n][];
            _boxes = new (float, float, float, float)[n];
            Depth = new float[n];
            _pairs = new sbyte[n * n];
        }
        for (var i = 0; i < n; i++)
        {
            if (Equals(_volumes[i], volumes[i])) continue;
            _volumes[i] = volumes[i];
            _hulls[i] = DepthOrder.ScreenHull(volumes[i], p);
            _boxes[i] = DepthOrder.Box(_hulls[i]);
            Depth[i] = p.Depth(volumes[i].Centre);
            for (var j = 0; j < n; j++)
            {
                _pairs[i * n + j] = Unknown;
                _pairs[j * n + i] = Unknown;
            }
        }
    }

    /// <summary>The same lists as <see cref="DepthOrder.Constraints"/>, in the same order, from the cached pairs.</summary>
    internal List<int>[] Constraints(IReadOnlyList<Volume> volumes, OrbitProjection p)
    {
        var n = volumes.Count;
        var after = new List<int>[n];
        for (var i = 0; i < n; i++) after[i] = [];
        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                ref var c = ref _pairs[i * n + j];
                if (c == Unknown) c = DepthOrder.Pair(volumes[i], volumes[j], _hulls[i], _hulls[j], _boxes[i], _boxes[j], p, Depth[i], Depth[j]);
                if (c == -1) after[i].Add(j);
                else if (c == 1) after[j].Add(i);
            }
        }
        return after;
    }
}
