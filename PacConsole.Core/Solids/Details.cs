using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>How a detail is drawn: a hairline, a bolt dot, or a moving flow dash along a pipe.</summary>
public enum DetailKind
{
    Line,
    Dot,
    Flow,
}

/// <summary>One line, dot or flow mark drawn over a solid, in screen space.</summary>
public readonly record struct DetailLine(EdgeTone Tone, Vec2[] Points, DetailKind Kind = DetailKind.Line);

/// <summary>
/// The figure's line work, ported from the kit (<c>ring</c>, <c>sideArc</c>, <c>sideSeam</c>,
/// <c>dotsSvg</c>, <c>topTicks</c>, the fan <c>blades</c>): drawn after a part's outline, in the
/// part's own frame, so it lifts with its unit.
/// </summary>
public static class Details
{
    private const float Tau = MathF.PI * 2;

    /// <summary>A full circle on a top face (always seen from above).</summary>
    public static DetailLine Ring(EdgeTone tone, float cx, float cz, float r, float y, OrbitProjection p, int steps = 40)
    {
        var points = new Vec2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = (float)i / steps * Tau;
            points[i] = p.Project(new(cx + r * MathF.Cos(a), y, cz + r * MathF.Sin(a)));
        }
        return new(tone, points);
    }

    /// <summary>The half of a horizontal circle on a cylinder's side that faces the viewer.</summary>
    public static DetailLine SideArc(EdgeTone tone, float cx, float cz, float r, float y, OrbitProjection p, int steps = 24)
    {
        var facing = MathF.Atan2(p.CosYaw, p.SinYaw); // the viewer's direction on the plan, as an angle in (X, Z)
        var points = new Vec2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var a = facing - MathF.PI / 2 + MathF.PI * i / steps;
            points[i] = p.Project(new(cx + r * MathF.Cos(a), y, cz + r * MathF.Sin(a)));
        }
        return new(tone, points);
    }

    /// <summary>A horizontal seam round a slab's sides at height <paramref name="y"/>, only where the sides face the viewer.</summary>
    public static IEnumerable<DetailLine> SideSeam(EdgeTone tone, Plan plan, float y, OrbitProjection p, int steps = 4)
    {
        var ring = plan.Ring(steps);
        var n = ring.Length;
        float vx = p.SinYaw, vz = p.CosYaw;
        var area = 0f;
        for (var i = 0; i < n; i++) area += ring[i].X * ring[(i + 1) % n].Z - ring[(i + 1) % n].X * ring[i].Z;
        var facing = new bool[n];
        for (var i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            float nx = area > 0 ? b.Z - a.Z : a.Z - b.Z, nz = area > 0 ? a.X - b.X : b.X - a.X;
            facing[i] = nx * vx + nz * vz > 1e-5f;
        }
        foreach (var run in SolidBuilder.RunsOf(facing, true))
        {
            yield return new(tone, [.. run.Select(i => p.Project(new(ring[i].X, y, ring[i].Z)))]);
        }
    }

    /// <summary>The outline of a plan drawn on a top face (a groove, a plate).</summary>
    public static DetailLine PlanOutline(EdgeTone tone, Plan plan, float y, OrbitProjection p, int steps = 8)
    {
        var ring = plan.Ring(steps);
        return new(tone, [.. ring.Append(ring[0]).Select(q => p.Project(new(q.X, y, q.Z)))]);
    }

    /// <summary>A straight line on a top face.</summary>
    public static DetailLine OnTop(EdgeTone tone, float x0, float z0, float x1, float z1, float y, OrbitProjection p) =>
        new(tone, [p.Project(new(x0, y, z0)), p.Project(new(x1, y, z1))]);

    /// <summary>A straight line between two points (fins, vertical ribs).</summary>
    public static DetailLine Segment(EdgeTone tone, Vec3 a, Vec3 b, OrbitProjection p) => new(tone, [p.Project(a), p.Project(b)]);

    /// <summary>Bolt heads.</summary>
    public static IEnumerable<DetailLine> Dots(EdgeTone tone, IEnumerable<Vec3> points, OrbitProjection p) =>
        points.Select(q => new DetailLine(tone, [p.Project(q)], DetailKind.Dot));

    /// <summary>Dots evenly round a circle on a top face (flange bolts, a gallery's posts).</summary>
    public static IEnumerable<DetailLine> DotRing(EdgeTone tone, float cx, float cz, float r, float y, int count, OrbitProjection p) =>
        Dots(tone, Enumerable.Range(0, count).Select(i => new Vec3(cx + r * MathF.Cos(i * Tau / count), y, cz + r * MathF.Sin(i * Tau / count))), p);

    /// <summary>A ruler's ticks along X on a top face: a minor tick every <paramref name="step"/>, a major one every <paramref name="every"/> ticks.</summary>
    public static IEnumerable<DetailLine> Ticks(float x0, float x1, float step, int every, float edgeZ, float y, float minor, float major, OrbitProjection p)
    {
        var k = 0;
        for (var x = x0; x <= x1 + 0.01f; x += step, k++)
        {
            var isMajor = k % every == 0;
            yield return OnTop(isMajor ? EdgeTone.Mid : EdgeTone.Lo, x, edgeZ, x, edgeZ + (isMajor ? major : minor), y, p);
        }
    }

    /// <summary>Five fan blades under a stack's guard, turned to <paramref name="angle"/> radians.</summary>
    public static IEnumerable<DetailLine> Blades(float cx, float cz, float y, float angle, OrbitProjection p)
    {
        for (var b = 0; b < 5; b++)
        {
            var t = angle + b * Tau / 5;
            yield return OnTop(EdgeTone.Mid, cx + MathF.Cos(t) * 1.4f, cz + MathF.Sin(t) * 1.4f, cx + MathF.Cos(t) * 6.3f, cz + MathF.Sin(t) * 6.3f, y, p);
        }
    }

    /// <summary>A flow mark along the crown of a straight pipe, from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static DetailLine Flow(Vec3 a, Vec3 b, float r, OrbitProjection p) =>
        new(EdgeTone.Hi, [p.Project(a with { Y = a.Y + r }), p.Project(b with { Y = b.Y + r })], DetailKind.Flow);

    /// <summary>True when a face with plan normal (nx, nz) faces the viewer.</summary>
    public static bool Faces(float nx, float nz, OrbitProjection p) => nx * p.SinYaw + nz * p.CosYaw > 1e-5f;

    /// <summary>Vertical lines every <paramref name="pitch"/> across a slab's visible sides (boiler wall tubes, tank fins).</summary>
    public static IEnumerable<DetailLine> FaceLines(Plan plan, float y0, float y1, float pitch, OrbitProjection p)
    {
        var faces = new (float Nx, float Nz, Vec3 A, Vec3 B)[]
        {
            (0, -1, new(plan.X, 0, plan.Z), new(plan.X + plan.W, 0, plan.Z)),
            (1, 0, new(plan.X + plan.W, 0, plan.Z), new(plan.X + plan.W, 0, plan.Z + plan.D)),
            (0, 1, new(plan.X, 0, plan.Z + plan.D), new(plan.X + plan.W, 0, plan.Z + plan.D)),
            (-1, 0, new(plan.X, 0, plan.Z), new(plan.X, 0, plan.Z + plan.D)),
        };
        foreach (var (nx, nz, a, b) in faces)
        {
            if (!Faces(nx, nz, p)) continue;
            var length = (b - a).Length;
            for (var s = plan.R + pitch; s < length - plan.R; s += pitch)
            {
                var x = a.X + (b.X - a.X) * s / length;
                var z = a.Z + (b.Z - a.Z) * s / length;
                yield return Details.Segment(EdgeTone.Faint, new(x, y0, z), new(x, y1, z), p);
            }
        }
    }

    /// <summary>A ladder standing off a face: two rails and a rung every 3 units, from <paramref name="foot"/> up to <paramref name="top"/>.</summary>
    public static IEnumerable<DetailLine> Ladder(Vec3 foot, Vec3 across, float width, float top, OrbitProjection p)
    {
        var other = new Vec3(foot.X + across.X * width, foot.Y, foot.Z + across.Z * width);
        yield return Details.Segment(EdgeTone.Mid, foot, foot with { Y = top }, p);
        yield return Details.Segment(EdgeTone.Mid, other, other with { Y = top }, p);
        for (var y = foot.Y + 2; y < top; y += 3)
        {
            yield return Details.Segment(EdgeTone.Lo, foot with { Y = y }, other with { Y = y }, p);
        }
    }

    /// <summary>A handrail round a deck: posts every <paramref name="pitch"/> and a top rail, <paramref name="height"/> tall.</summary>
    public static IEnumerable<DetailLine> Handrail(Plan deck, float y, float height, float pitch, OrbitProjection p)
    {
        var (x0, z0, x1, z1) = (deck.X + 0.6f, deck.Z + 0.6f, deck.X + deck.W - 0.6f, deck.Z + deck.D - 0.6f);
        PlanPoint[] ring = [new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1)];
        yield return new(EdgeTone.Mid, [.. ring.Select(q => p.Project(new(q.X, y + height, q.Z))), p.Project(new(ring[0].X, y + height, ring[0].Z))]);
        yield return new(EdgeTone.Lo, [.. ring.Select(q => p.Project(new(q.X, y + height / 2, q.Z))), p.Project(new(ring[0].X, y + height / 2, ring[0].Z))]);
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Length];
            var length = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            for (var s = 0f; s < length; s += pitch)
            {
                var x = a.X + (b.X - a.X) * s / length;
                var z = a.Z + (b.Z - a.Z) * s / length;
                yield return Details.Segment(EdgeTone.Lo, new(x, y, z), new(x, y + height, z), p);
            }
        }
    }
}
