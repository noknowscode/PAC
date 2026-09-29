using PacConsole.Core.Plant;

namespace PacConsole.Core.Geometry;

/// <summary>
/// The plant as wireframe segments, built once. A port of the prototype's <c>geo()</c>:
/// same primitives, same coordinates, so screenshots line up with the HTML prototype.
/// </summary>
public sealed class PlantGeometry
{
    public const float PlatformHalfX = 104, PlatformHalfZ = 72;

    private static readonly Lazy<PlantGeometry> _default = new(Build);

    /// <summary>The one plant this app draws, built on first use and shared by every view.</summary>
    public static PlantGeometry Default => _default.Value;

    private PlantGeometry(
        ImmutableDictionary<UnitId, ImmutableArray<Segment3>> units,
        ImmutableArray<Segment3> grid,
        ImmutableArray<Segment3> structure,
        ImmutableDictionary<UnitId, Vec3> anchors)
    {
        Units = units;
        Grid = grid;
        Structure = structure;
        Anchors = anchors;

        var centers = ImmutableDictionary.CreateBuilder<UnitId, Vec3>();
        var radii = ImmutableDictionary.CreateBuilder<UnitId, float>();
        foreach (var (id, segs) in units)
        {
            var (center, radius) = Bounds(segs);
            centers[id] = center;
            radii[id] = radius;
        }
        Centers = centers.ToImmutable();
        Radii = radii.ToImmutable();
    }

    public ImmutableDictionary<UnitId, ImmutableArray<Segment3>> Units { get; }

    /// <summary>Floor grid lines, drawn faintest.</summary>
    public ImmutableArray<Segment3> Grid { get; }

    /// <summary>Platform slab and pipe racks: drawn like units but never selectable.</summary>
    public ImmutableArray<Segment3> Structure { get; }

    /// <summary>Where each unit's floating tag attaches (top of the unit).</summary>
    public ImmutableDictionary<UnitId, Vec3> Anchors { get; }

    public ImmutableDictionary<UnitId, Vec3> Centers { get; }

    /// <summary>Bounding-sphere radius per unit, for fitting a single unit into a thumbnail.</summary>
    public ImmutableDictionary<UnitId, float> Radii { get; }

    /// <summary>Every segment: structure plus all units. Used to fit whole-plant thumbnails.</summary>
    public IEnumerable<Segment3> AllSolid => Structure.Concat(Units.Values.SelectMany(s => s));

    public static PlantGeometry Build()
    {
        var units = ImmutableDictionary.CreateBuilder<UnitId, ImmutableArray<Segment3>>();

        units[UnitId.Stk] =
        [
            .. Cyl(-36, -40, 4, 0, 96, n: 14, rings: 7, verts: 4), .. Ring(-36, -40, 6.5f, 72, 14),
            .. Cyl(-16, -44, 4.6f, 0, 106, n: 14, rings: 8, verts: 4), .. Ring(-16, -44, 7, 80, 14),
            .. Box(-26, -42, 34, 14, 0, 10, bays: 3),
        ];

        units[UnitId.Tka] =
        [
            .. Cyl(-86, -46, 6.5f, 0, 32, rings: 3, verts: 6, dome: 4),
            .. Cyl(-70, -46, 6.5f, 0, 32, rings: 3, verts: 6, dome: 4),
            .. Cyl(-78, -30, 6.5f, 0, 26, rings: 2, verts: 6, dome: 4),
            .. Box(-84, -16, 14, 8, 0, 6, bays: 1),
        ];

        units[UnitId.Gen] =
        [
            .. Box(-24, -14, 44, 30, 0, 40, floors: 4, bays: 5, dbays: 3),
            .. Box(-54, -8, 12, 20, 0, 22, floors: 2, bays: 1),
            .. Box(-34, 14, 10, 8, 0, 9, bays: 1),
            .. Box(-18, 14, 10, 8, 0, 9, bays: 1),
        ];

        var trb = new List<Segment3>(Box(24, -20, 34, 26, 0, 30, floors: 2, bays: 4));
        for (var k = 0; k <= 5; k++)
        {
            var x = 7 + 34f * k / 5;
            trb.AddRange(Poly(new(x, 30, -33), new(x, 38, -20), new(x, 30, -7)));
        }
        trb.Add(new(new(7, 38, -20), new(41, 38, -20)));
        trb.AddRange(Box(24, 4, 22, 14, 0, 16, bays: 2));
        units[UnitId.Trb] = [.. trb];

        var cnd = new List<Segment3>(Box(36, 36, 44, 18, 0, 12, bays: 5));
        foreach (var fx in new float[] { 20, 31, 42, 53 })
        {
            cnd.AddRange(Cyl(fx, 36, 3.6f, 12, 3, n: 12, verts: 4));
        }
        units[UnitId.Cnd] = [.. cnd];

        var sil = new List<Segment3>(Cyl(76, -26, 12, 0, 42, n: 22, rings: 4, verts: 10, dome: 8));
        sil.AddRange(Box(76, -4, 18, 10, 0, 8, bays: 1));
        sil.AddRange(Poly(new(64, 40, -27.5f), new(41, 26, -21.5f)));
        sil.AddRange(Poly(new(64, 40, -24.5f), new(41, 26, -18.5f)));
        for (var k = 0; k <= 6; k++)
        {
            var f = k / 6f;
            var p = new Vec3(64 - 23 * f, 40 - 14 * f, -27.5f + 6 * f);
            sil.Add(new(p, p with { Z = p.Z + 3 }));
        }
        units[UnitId.Sil] = [.. sil];

        const float x0 = -PlatformHalfX, x1 = PlatformHalfX, z0 = -PlatformHalfZ, z1 = PlatformHalfZ;
        var grid = new List<Segment3>();
        for (var x = x0; x <= x1 + 0.01f; x += 8) grid.Add(new(new(x, 0, z0), new(x, 0, z1)));
        for (var z = z0; z <= z1 + 0.01f; z += 8) grid.Add(new(new(x0, 0, z), new(x1, 0, z)));

        var structure = new List<Segment3>();
        structure.AddRange(Rect(x0, x1, z0, z1, 0));
        structure.AddRange(Rect(x0, x1, z0, z1, -5));
        foreach (var (cx, cz) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) })
        {
            structure.Add(new(new(cx, 0, cz), new(cx, -5, cz)));
        }
        structure.AddRange(Poly(new(-63.5f, 14, -46), new(-50, 14, -46), new(-50, 14, -29)));
        structure.AddRange(Poly(new(-63.5f, 11, -46), new(-52, 11, -46), new(-52, 11, -29)));
        structure.AddRange(Poly(new(-2, 18, -18), new(7, 18, -18)));
        structure.AddRange(Poly(new(-2, 15, -18), new(7, 15, -18)));
        structure.AddRange(Poly(new(30, 10, -7), new(30, 10, 27)));
        structure.AddRange(Poly(new(34, 10, -7), new(34, 10, 27)));

        var anchors = new Dictionary<UnitId, Vec3>
        {
            [UnitId.Stk] = new(-16, 110, -44),
            [UnitId.Tka] = new(-78, 40, -46),
            [UnitId.Gen] = new(-24, 44, -14),
            [UnitId.Trb] = new(24, 42, -20),
            [UnitId.Cnd] = new(36, 18, 36),
            [UnitId.Sil] = new(76, 54, -26),
        }.ToImmutableDictionary();

        return new PlantGeometry(units.ToImmutable(), [.. grid], [.. structure], anchors);
    }

    private static (Vec3 Center, float Radius) Bounds(ImmutableArray<Segment3> segs)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var s in segs)
        {
            foreach (var p in new[] { s.A, s.B })
            {
                minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
                minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
                minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
            }
        }
        var c = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
        var r = 0f;
        foreach (var s in segs)
        {
            r = MathF.Max(r, MathF.Max((s.A - c).Length, (s.B - c).Length));
        }
        return (c, r);
    }

    private static IEnumerable<Segment3> Ring(float cx, float cz, float r, float y, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var a = i / (float)n * MathF.Tau;
            var b = (i + 1) / (float)n * MathF.Tau;
            yield return new(
                new(cx + r * MathF.Cos(a), y, cz + r * MathF.Sin(a)),
                new(cx + r * MathF.Cos(b), y, cz + r * MathF.Sin(b)));
        }
    }

    private static IEnumerable<Segment3> Cyl(float cx, float cz, float r, float y0, float h,
        int n = 18, int rings = 0, int verts = 6, float dome = 0)
    {
        foreach (var s in Ring(cx, cz, r, y0, n)) yield return s;
        foreach (var s in Ring(cx, cz, r, y0 + h, n)) yield return s;
        for (var k = 1; k <= rings; k++)
        {
            foreach (var s in Ring(cx, cz, r, y0 + h * k / (rings + 1), n)) yield return s;
        }
        for (var i = 0; i < verts; i++)
        {
            var a = i / (float)verts * MathF.Tau + 0.3f;
            var x = cx + r * MathF.Cos(a);
            var z = cz + r * MathF.Sin(a);
            yield return new(new(x, y0, z), new(x, y0 + h, z));
        }
        if (dome <= 0) yield break;

        var top = y0 + h;
        for (var k = 1; k <= 3; k++)
        {
            var t = k / 4f * MathF.PI / 2;
            foreach (var s in Ring(cx, cz, r * MathF.Cos(t), top + dome * MathF.Sin(t), n)) yield return s;
        }
        for (var i = 0; i < verts; i++)
        {
            var a = i / (float)verts * MathF.Tau + 0.3f;
            Vec3? prev = null;
            for (var k = 0; k <= 4; k++)
            {
                var t = k / 4f * MathF.PI / 2;
                var p = new Vec3(
                    cx + r * MathF.Cos(t) * MathF.Cos(a),
                    top + dome * MathF.Sin(t),
                    cz + r * MathF.Cos(t) * MathF.Sin(a));
                if (prev is { } q) yield return new(q, p);
                prev = p;
            }
        }
    }

    private static IEnumerable<Segment3> Rect(float x0, float x1, float z0, float z1, float y)
    {
        yield return new(new(x0, y, z0), new(x1, y, z0));
        yield return new(new(x1, y, z0), new(x1, y, z1));
        yield return new(new(x1, y, z1), new(x0, y, z1));
        yield return new(new(x0, y, z1), new(x0, y, z0));
    }

    private static IEnumerable<Segment3> Box(float cx, float cz, float w, float d, float y0, float h,
        int floors = 0, int bays = 0, int dbays = 0)
    {
        float x0 = cx - w / 2, x1 = cx + w / 2, z0 = cz - d / 2, z1 = cz + d / 2, y1 = y0 + h;
        foreach (var s in Rect(x0, x1, z0, z1, y0)) yield return s;
        foreach (var s in Rect(x0, x1, z0, z1, y1)) yield return s;
        foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x1, z1), (x0, z1) })
        {
            yield return new(new(x, y0, z), new(x, y1, z));
        }
        for (var k = 1; k <= floors; k++)
        {
            foreach (var s in Rect(x0, x1, z0, z1, y0 + h * k / (floors + 1))) yield return s;
        }
        for (var k = 1; k <= bays; k++)
        {
            var x = x0 + w * k / (bays + 1);
            yield return new(new(x, y0, z0), new(x, y1, z0));
            yield return new(new(x, y0, z1), new(x, y1, z1));
            yield return new(new(x, y1, z0), new(x, y1, z1));
        }
        for (var k = 1; k <= dbays; k++)
        {
            var z = z0 + d * k / (dbays + 1);
            yield return new(new(x0, y0, z), new(x0, y1, z));
            yield return new(new(x1, y0, z), new(x1, y1, z));
        }
    }

    private static IEnumerable<Segment3> Poly(params Vec3[] pts)
    {
        for (var i = 0; i < pts.Length - 1; i++) yield return new(pts[i], pts[i + 1]);
    }
}
