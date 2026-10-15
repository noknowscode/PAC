using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>An axis frame for turned parts: origin, axis and the two radial directions.</summary>
public readonly record struct Frame(Vec3 O, Vec3 A, Vec3 U, Vec3 V)
{
    public Vec3 PointAt(float s, float r, float angle)
    {
        var c = MathF.Cos(angle);
        var n = MathF.Sin(angle);
        return new(
            O.X + A.X * s + r * (c * U.X + n * V.X),
            O.Y + A.Y * s + r * (c * U.Y + n * V.Y),
            O.Z + A.Z * s + r * (c * U.Z + n * V.Z));
    }

    /// <summary>A vertical axis through (x, z) on the floor, radial directions along X and Z.</summary>
    public static Frame Vertical(float x, float z) => new(new(x, 0, z), new(0, 1, 0), new(1, 0, 0), new(0, 0, 1));

    /// <summary>An axis from <paramref name="o"/> along <paramref name="a"/>, with any radial pair (the kit's <c>frameAlong</c>).</summary>
    public static Frame Along(Vec3 o, Vec3 a)
    {
        var axis = Unit(a);
        // The kit's helper is its Z (up), which is the app's Y.
        var helper = MathF.Abs(axis.Y) > 0.9f ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var u = Unit(Cross(helper, axis));
        return new(o, axis, u, Cross(axis, u));
    }

    private static Vec3 Unit(Vec3 v) => new(v.X / v.Length, v.Y / v.Length, v.Z / v.Length);

    private static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

/// <summary>
/// The Anatomy kit's solid builders (<c>iso-kit.mjs</c> extrude/slabOf/cylinder, <c>lathe.mjs</c>
/// lathe/disc/solidOf), ported to the app's axes: Y up, the figure's (x, y) plan as (X, Z).
/// Hard-knot profiles only; the plant figure uses no smooth-shaded profiles.
/// </summary>
public static class SolidBuilder
{
    private const float Tau = MathF.PI * 2;
    private const float LightUp = 1.2f;
    private static readonly float[] Bands = [0, 0.2f, 0.42f, 0.62f];

    public static SolidShape Slab(Plan plan, float y, float height, OrbitProjection p, int steps = 8, float bevel = 0) =>
        Extrude(plan.Ring(steps), y, height, p, bevel);

    public static SolidShape Cylinder(float cx, float cz, float radius, float y, float height, OrbitProjection p, int steps = 32, float bevel = 0) =>
        Extrude(CircleRing(cx, cz, radius, steps), y, height, p, bevel);

    public static PlanPoint[] CircleRing(float cx, float cz, float radius, int steps = 40)
    {
        var ring = new PlanPoint[steps];
        for (var i = 0; i < steps; i++)
        {
            var angle = (float)i / steps * Tau;
            ring[i] = new(cx + radius * MathF.Cos(angle), cz + radius * MathF.Sin(angle));
        }
        return ring;
    }

    /// <summary>A convex plan ring extruded up from <paramref name="y"/>.</summary>
    public static SolidShape Extrude(PlanPoint[] ring, float y, float height, OrbitProjection p, float bevel = 0)
    {
        var count = ring.Length;
        var counter = SignedArea(ring) > 0;
        float vx = p.SinYaw, vz = p.CosYaw;   // toward the viewer, on the plan
        float lx = -p.CosYaw, lz = p.SinYaw;  // the light, on the plan: from the viewer's left

        var normals = new PlanPoint[count];
        var facing = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % count];
            float dx = b.X - a.X, dz = b.Z - a.Z;
            var length = MathF.Sqrt(dx * dx + dz * dz);
            if (length == 0) length = 1;
            normals[i] = counter ? new(dz / length, -dx / length) : new(-dz / length, dx / length);
            facing[i] = normals[i].X * vx + normals[i].Z * vz > 1e-5f; // float noise on an edge-on face
        }

        var top = new Vec2[count];
        var bottom = new Vec2[count];
        for (var i = 0; i < count; i++)
        {
            top[i] = p.Project(new(ring[i].X, y + height, ring[i].Z));
            bottom[i] = p.Project(new(ring[i].X, y, ring[i].Z));
        }

        var outline = new List<Vec2[]>();
        var crease = new List<Vec2[]>();
        foreach (var run in RunsOf(facing, false)) outline.Add(Pick(top, run));
        foreach (var run in RunsOf(facing, true))
        {
            crease.Add(Pick(top, run));
            if (height > 0) outline.Add(Pick(bottom, run));
        }
        if (height > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (facing[(i + count - 1) % count] != facing[i]) outline.Add([top[i], bottom[i]]);
            }
        }

        var shades = NewBuckets(SolidShape.Shades);
        if (height > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (!facing[i]) continue;
                var lit = (normals[i].X * lx + normals[i].Z * lz + 1) / 2;
                var shade = Math.Clamp((int)MathF.Floor(lit * SolidShape.Shades), 0, SolidShape.Shades - 1);
                var next = (i + 1) % count;
                shades[shade].Add(Oriented([top[i], top[next], bottom[next], bottom[i]]));
            }
        }

        var bevels = new List<Vec2[]>();
        if (bevel > 0)
        {
            var insetTop = new Vec2[count];
            for (var i = 0; i < count; i++)
            {
                var na = normals[(i + count - 1) % count];
                var nb = normals[i];
                float mx = na.X + nb.X, mz = na.Z + nb.Z;
                var length = MathF.Sqrt(mx * mx + mz * mz);
                if (length == 0) length = 1;
                insetTop[i] = p.Project(new(ring[i].X - mx / length * bevel, y + height, ring[i].Z - mz / length * bevel));
            }
            foreach (var run in RunsOf(facing, true))
            {
                var line = new Vec2[run.Count];
                for (var k = 0; k < run.Count; k++)
                {
                    line[k] = k == 0 || k == run.Count - 1 ? top[run[k]] : insetTop[run[k]];
                }
                bevels.Add(line);
            }
        }

        return new SolidShape
        {
            Fill = ConvexHull([.. top, .. bottom]),
            ShadeFaces = shades,
            Top = [Oriented(top)],
            Outline = outline,
            Crease = crease,
            Bevel = bevels,
        };
    }

    /// <summary>A disc (a short cylinder) between <paramref name="s0"/> and <paramref name="s1"/> along a frame's axis.</summary>
    public static SolidShape Disc(float s0, float s1, float r, Frame f, OrbitProjection p) =>
        Lathe([(s0, 0), (s0, r), (s1, r), (s1, 0)], f, p, StepsFor(r, 32, 160, p.Scale));

    /// <summary>A closed turned solid from a profile of (s, r) knots: the kit's <c>solidOf</c>.</summary>
    public static SolidShape Turned((float S, float R)[] profile, Frame f, OrbitProjection p)
    {
        var most = profile.Max(k => k.R);
        return Lathe([(profile[0].S, 0), .. profile, (profile[^1].S, 0)], f, p, StepsFor(most, 32, 160, p.Scale));
    }

    /// <summary>
    /// A straight pipe from <paramref name="a"/> to <paramref name="b"/>: a disc along its own axis,
    /// shaded like every other turned part. (The kit draws tubes as flat ribbons; solids need a hull.)
    /// </summary>
    public static SolidShape Tube(Vec3 a, Vec3 b, float r, OrbitProjection p)
    {
        var along = b - a;
        return Disc(0, along.Length, r, Frame.Along(a, along), p);
    }

    /// <summary>
    /// A polygon in a frame's (u, v) plane swept along its axis from <paramref name="s0"/> to
    /// <paramref name="s1"/>: the kit's <c>prismOf</c> (the generator hall's gable roof).
    /// </summary>
    public static SolidShape Prism(Frame f, (float X, float Y)[] polygon, float s0, float s1, OrbitProjection p)
    {
        var view = ViewOf(p);
        var light = LightOf(p);
        var ring = polygon.ToArray();
        var area = 0f;
        for (var i = 0; i < ring.Length; i++) area += ring[i].X * ring[(i + 1) % ring.Length].Y - ring[(i + 1) % ring.Length].X * ring[i].Y;
        if (area < 0) Array.Reverse(ring);
        var n = ring.Length;
        Vec3 At(float s, (float X, float Y) q) => new(
            f.O.X + f.A.X * s + f.U.X * q.X + f.V.X * q.Y,
            f.O.Y + f.A.Y * s + f.U.Y * q.X + f.V.Y * q.Y,
            f.O.Z + f.A.Z * s + f.U.Z * q.X + f.V.Z * q.Y);
        var low = ring.Select(q => p.Project(At(s0, q))).ToArray();
        var high = ring.Select(q => p.Project(At(s1, q))).ToArray();

        var buckets = NewBuckets(5);
        var seen = new bool[n];
        for (var j = 0; j < n; j++)
        {
            var (x0, y0) = ring[j];
            var (x1, y1) = ring[(j + 1) % n];
            var normal = Normalize(new(
                f.U.X * (y1 - y0) - f.V.X * (x1 - x0),
                f.U.Y * (y1 - y0) - f.V.Y * (x1 - x0),
                f.U.Z * (y1 - y0) - f.V.Z * (x1 - x0)));
            seen[j] = Dot(normal, view) > 1e-6f;
            if (seen[j]) buckets[BandOf(Dot(normal, light))].Add([low[j], low[(j + 1) % n], high[(j + 1) % n], high[j]]);
        }
        var back = new Vec3(-f.A.X, -f.A.Y, -f.A.Z);
        (Vec2[] Points, bool Seen)[] ends = [(low, Dot(back, view) > 1e-6f), (high, Dot(f.A, view) > 1e-6f)];
        if (ends[0].Seen) buckets[BandOf(Dot(back, light))].Add(low);
        if (ends[1].Seen) buckets[BandOf(Dot(f.A, light))].Add(high);

        var crease = new List<Vec2[]>();
        for (var j = 0; j < n; j++)
        {
            var prev = (j - 1 + n) % n;
            float ax = ring[j].X - ring[prev].X, ay = ring[j].Y - ring[prev].Y;
            float bx = ring[(j + 1) % n].X - ring[j].X, by = ring[(j + 1) % n].Y - ring[j].Y;
            var lengths = MathF.Sqrt(ax * ax + ay * ay) * MathF.Sqrt(bx * bx + by * by);
            var sharp = (ax * bx + ay * by) / (lengths == 0 ? 1 : lengths) < MathF.Cos(0.35f);
            if (sharp && seen[j] && seen[prev]) crease.Add([low[j], high[j]]);
            foreach (var (points, capSeen) in ends)
            {
                if (capSeen && seen[j]) crease.Add([points[j], points[(j + 1) % n]]);
            }
        }

        var hull = ConvexHull([.. low, .. high]);
        return new SolidShape
        {
            Fill = hull,
            ShadeFaces = [buckets[0], buckets[1], buckets[2], buckets[3]],
            Top = buckets[4],
            Outline = [[.. hull, hull[0]]],
            Crease = crease,
            Bevel = [],
        };
    }

    private static Vec3 ViewOf(OrbitProjection p) => new(p.SinYaw * p.CosElevation, p.SinElevation, p.CosYaw * p.CosElevation);

    private static Vec3 LightOf(OrbitProjection p) => Normalize(new(-p.CosYaw, LightUp, p.SinYaw));

    public static int StepsFor(float radius, int least, int most, float scale) =>
        Math.Max(least, Math.Min(most, (int)MathF.Ceiling(Tau * radius * scale / 3)));

    /// <summary>
    /// Turns a meridian of (s, r) knots round a frame's axis. Each facet gets the kit's light band:
    /// bands 0-3 are side shades, band 4 faces the light and paints as a top.
    /// </summary>
    public static SolidShape Lathe((float S, float R)[] meridian, Frame f, OrbitProjection p, int steps)
    {
        var view = ViewOf(p);
        var light = LightOf(p);
        float aV = Dot(f.A, view), uV = Dot(f.U, view), vV = Dot(f.V, view);
        float aL = Dot(f.A, light), uL = Dot(f.U, light), vL = Dot(f.V, light);
        var n = steps;
        var m = meridian.Length;

        var rings = new Vec2[m][];
        for (var i = 0; i < m; i++)
        {
            rings[i] = new Vec2[n];
            for (var j = 0; j < n; j++) rings[i][j] = p.Project(f.PointAt(meridian[i].S, meridian[i].R, (float)j / n * Tau));
        }

        var buckets = NewBuckets(5);
        for (var i = 0; i < m - 1; i++)
        {
            var ds = meridian[i + 1].S - meridian[i].S;
            var dr = meridian[i + 1].R - meridian[i].R;
            var length = MathF.Sqrt(ds * ds + dr * dr);
            if (length < 1e-9f) continue;
            for (var j = 0; j < n; j++)
            {
                var t = (j + 0.5f) / n * Tau;
                float c = MathF.Cos(t), s = MathF.Sin(t);
                var facing = ds * (c * uV + s * vV) - dr * aV;
                if (facing <= 1e-6f * length) continue;
                var lit = (ds * (c * uL + s * vL) - dr * aL) / length;
                var next = (j + 1) % n;
                buckets[BandOf(lit)].Add(Oriented([rings[i][j], rings[i][next], rings[i + 1][next], rings[i + 1][j]]));
            }
        }

        // Creases: the visible arc of every inner knot ring.
        var phi = MathF.Atan2(vV, uV);
        var flatV = MathF.Sqrt(uV * uV + vV * vV);
        (float Lo, float Hi)? RangeOf(int i)
        {
            var ds = meridian[i + 1].S - meridian[i].S;
            var dr = meridian[i + 1].R - meridian[i].R;
            if (MathF.Sqrt(ds * ds + dr * dr) < 1e-9f) return null;
            if (MathF.Abs(ds) < 1e-9f || flatV < 1e-9f) return -dr * aV > 1e-9f ? (-1, 1) : null;
            var c = dr * aV / (ds * flatV);
            return ds > 0 ? (MathF.Max(-1, c), 1) : (-1, MathF.Min(1, c));
        }
        List<(float From, float To)> ArcsOf((float Lo, float Hi)? r0, (float Lo, float Hi)? r1)
        {
            if (r0 is not { } a || r1 is not { } b) return [];
            var lo = MathF.Max(a.Lo, b.Lo);
            var hi = MathF.Min(a.Hi, b.Hi);
            if (lo >= hi - 1e-9f) return [];
            var near = MathF.Acos(MathF.Min(1, hi));
            var far = MathF.Acos(MathF.Max(-1, lo));
            if (hi >= 1 && lo <= -1) return [(phi, phi + Tau)];
            if (hi >= 1) return [(phi - far, phi + far)];
            if (lo <= -1) return [(phi + near, phi + Tau - near)];
            return [(phi + near, phi + far), (phi - far, phi - near)];
        }
        var crease = new List<Vec2[]>();
        for (var i = 1; i < m - 1; i++)
        {
            var (s, r) = meridian[i];
            if (r <= 1e-9f) continue;
            foreach (var (from, to) in ArcsOf(RangeOf(i - 1), RangeOf(i)))
            {
                var count = Math.Max(2, (int)MathF.Ceiling((to - from) * MathF.Max(r, 1e-6f) * p.Scale / 1.5f));
                var arc = new Vec2[count + 1];
                for (var j = 0; j <= count; j++) arc[j] = p.Project(f.PointAt(s, r, from + (to - from) * j / count));
                crease.Add(arc);
            }
        }

        // Silhouette: the hull of every knot ring, sampled finely enough to stay round on screen.
        var mostR = meridian.Max(k => k.R);
        var around = Math.Min(320, Math.Max(n, (int)MathF.Ceiling(Tau * mostR * p.Scale / 1.5f)));
        var all = new List<Vec2>(m * around);
        foreach (var (s, r) in meridian)
        {
            for (var j = 0; j < around; j++) all.Add(p.Project(f.PointAt(s, r, (float)j / around * Tau)));
        }
        var hull = ConvexHull(all);

        return new SolidShape
        {
            Fill = hull,
            ShadeFaces = [buckets[0], buckets[1], buckets[2], buckets[3]],
            Top = buckets[4],
            Outline = [[.. hull, hull[0]]],
            Crease = crease,
            Bevel = [],
        };
    }

    private static int BandOf(float score)
    {
        var tone = 0;
        while (tone < Bands.Length && score >= Bands[tone]) tone++;
        return tone;
    }

    private static List<Vec2[]>[] NewBuckets(int count)
    {
        var buckets = new List<Vec2[]>[count];
        for (var i = 0; i < count; i++) buckets[i] = [];
        return buckets;
    }

    private static Vec2[] Pick(Vec2[] points, List<int> run)
    {
        var picked = new Vec2[run.Count];
        for (var i = 0; i < run.Count; i++) picked[i] = points[run[i]];
        return picked;
    }

    /// <summary>
    /// Runs of consecutive edges whose flag equals <paramref name="want"/>, as vertex indices
    /// (a run of k edges lists k + 1 vertices). Wraps round the ring.
    /// </summary>
    internal static List<List<int>> RunsOf(bool[] flags, bool want)
    {
        var count = flags.Length;
        var runs = new List<List<int>>();
        var start = -1;
        for (var i = 0; i < count; i++)
        {
            if (flags[i] != flags[(i + count - 1) % count]) { start = i; break; }
        }
        if (start < 0)
        {
            if (flags[0] == want) runs.Add([.. Enumerable.Range(0, count + 1).Select(i => i % count)]);
            return runs;
        }
        List<int>? run = null;
        for (var step = 0; step < count; step++)
        {
            var index = (start + step) % count;
            if (flags[index] == want)
            {
                run ??= [index];
                run.Add((index + 1) % count);
            }
            else if (run is not null)
            {
                runs.Add(run);
                run = null;
            }
        }
        if (run is not null) runs.Add(run);
        return runs;
    }

    private static float SignedArea(PlanPoint[] ring)
    {
        var area = 0f;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            area += a.X * b.Z - b.X * a.Z;
        }
        return area / 2;
    }

    private static float SignedArea(Vec2[] points)
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area / 2;
    }

    /// <summary>Every face wound the same way, so a path of many faces fills as their union.</summary>
    private static Vec2[] Oriented(Vec2[] points)
    {
        if (SignedArea(points) < 0) Array.Reverse(points);
        return points;
    }

    /// <summary>Andrew's monotone chain, as the kit's <c>convexHull</c>.</summary>
    public static Vec2[] ConvexHull(IReadOnlyCollection<Vec2> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3) return sorted;
        static float Cross(Vec2 o, Vec2 a, Vec2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Vec2>(sorted.Length);
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

    private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vec3 Normalize(Vec3 v)
    {
        var length = v.Length;
        return new(v.X / length, v.Y / length, v.Z / length);
    }
}
