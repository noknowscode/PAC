using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>Line tones of the reference figure, brightest first.</summary>
public enum EdgeTone
{
    Hi,
    Mid,
    Lo,
    Faint,
}

/// <summary>
/// One solid of the plant: its hull for ordering and a builder that draws it for a camera.
/// <see cref="Lit"/> parts take the selection colours when their unit is selected.
/// </summary>
public sealed record SolidPart(string Name, UnitId? Unit, Volume Volume, Func<OrbitProjection, SolidShape> Build, EdgeTone Edge = EdgeTone.Mid, bool Lit = false, bool Crease = true)
{
    /// <summary>Line work drawn over the part (seams, bolts, fan blades), given the camera and the time in seconds.</summary>
    public Func<OrbitProjection, double, IEnumerable<DetailLine>>? Details { get; init; }

    /// <summary>Set on a pad: the unit standing on it, whose lift shadow is drawn on this pad.</summary>
    public UnitId? PadOf { get; init; }
}

/// <summary>
/// The plant as solids, from the reference figure's <c>docs/anatomy/plant/build.mjs</c>. The figure
/// is Z-up; here the figure's (x, y, z) is (X, Z, Y), so every plan keeps the figure's numbers.
/// Line work (seams, bolts, ticks, fan blades, pipe flow) rides on its part as <see cref="SolidPart.Details"/>.
/// </summary>
public sealed class PlantSolids
{
    public const float PadHeight = 1.2f;
    public const float PadHalf = 36;
    public const float PlateHeight = 10, FootHeight = 6;
    public static readonly Plan Plate = new(-160, -110, 320, 220, 10);

    private static readonly Lazy<PlantSolids> _default = new(() => new PlantSolids());

    /// <summary>The one plant this app draws, built on first use.</summary>
    public static PlantSolids Default => _default.Value;

    /// <summary>Unit centres on the plan, from the figure's UNITS table.</summary>
    public static readonly ImmutableDictionary<UnitId, PlanPoint> Centres = new Dictionary<UnitId, PlanPoint>
    {
        [UnitId.Stk] = new(-95, -48),
        [UnitId.Tka] = new(0, -48),
        [UnitId.Gen] = new(95, -48),
        [UnitId.Trb] = new(0, 48),
        [UnitId.Cnd] = new(95, 48),
        [UnitId.Sil] = new(-95, 48),
    }.ToImmutableDictionary();

    private readonly ImmutableArray<SolidPart>.Builder _parts = ImmutableArray.CreateBuilder<SolidPart>();
    private readonly Dictionary<UnitId, SolidPart> _pads = [];

    private PlantSolids()
    {
        _unitCorners = new(() => Centres.Keys.ToDictionary(id => id, id => Corners(All.Where(p => p.Unit == id)).ToArray()));
        Stand();
        Stacks();
        TankFarm();
        GeneratorHall();
        TurbineHouse();
        CondenserBank();
        AshSilo();
        Pipework();
        All = _parts.ToImmutable();
        Pads = _pads.ToImmutableDictionary();

        // Each tag hangs from the top of its unit's tallest part, so the leader lands on the unit.
        Anchors = Centres.Keys.ToImmutableDictionary(id => id, id =>
        {
            var top = All.Where(p => p.Unit == id).MaxBy(p => p.Volume.Y1)!.Volume;
            var c = top.Centre;
            return new Vec3(c.X, top.Y1, c.Z);
        });
    }

    /// <summary>Where each unit's tag attaches: the top centre of its tallest part.</summary>
    public ImmutableDictionary<UnitId, Vec3> Anchors { get; }

    /// <summary>
    /// The camera framing every whole-plant view shares, so the drawing and the tags placed over it
    /// use the same projection. The sphere holds the plate, feet and the tallest stack.
    /// </summary>
    public static OrbitProjection ViewProjection(CameraPose pose, float width, float height, float padding = 8) =>
        OrbitProjection.AroundSphere(pose, new(0, 20, 0), 190, width, height, padding);

    /// <summary>Fits the parts' hulls tightly inside a box, for fixed-camera thumbnails.</summary>
    public static OrbitProjection Fit(CameraPose pose, IEnumerable<SolidPart> parts, float width, float height, float padding)
    {
        var unit = new OrbitProjection(pose, 1, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var q in Corners(parts))
        {
            var s = unit.Project(q);
            minX = MathF.Min(minX, s.X); maxX = MathF.Max(maxX, s.X);
            minY = MathF.Min(minY, s.Y); maxY = MathF.Max(maxY, s.Y);
        }
        var scale = MathF.Min((width - 2 * padding) / (maxX - minX), (height - 2 * padding) / (maxY - minY));
        return new OrbitProjection(pose, scale, width / 2 - scale * (minX + maxX) / 2, height / 2 - scale * (minY + maxY) / 2);
    }

    /// <summary>Centre and radius that hold one unit at any yaw, for the turning unit preview.</summary>
    public (Vec3 Centre, float Radius) Sphere(UnitId id)
    {
        var corners = Corners(UnitWithPad(id)).ToList();
        var centre = new Vec3(
            (corners.Min(q => q.X) + corners.Max(q => q.X)) / 2,
            (corners.Min(q => q.Y) + corners.Max(q => q.Y)) / 2,
            (corners.Min(q => q.Z) + corners.Max(q => q.Z)) / 2);
        return (centre, corners.Max(q => (q - centre).Length));
    }

    /// <summary>
    /// The unit under a screen point: each unit's hit area is the hull of its parts on screen
    /// (raised by its lift), and where areas overlap the unit nearest the viewer wins.
    /// </summary>
    public UnitId? UnitAt(OrbitProjection p, Vec2 point, IReadOnlyDictionary<UnitId, float>? lift = null)
    {
        UnitId? best = null;
        var bestDepth = float.MinValue;
        foreach (var (id, corners) in _unitCorners.Value)
        {
            var raise = lift is not null && lift.TryGetValue(id, out var l) ? l : 0;
            var hull = SolidBuilder.ConvexHull([.. corners.Select(q => p.Project(q with { Y = q.Y + raise }))]);
            if (!Contains(hull, point)) continue;
            var c = Centres[id];
            var depth = p.Depth(new(c.X, 0, c.Z));
            if (depth > bestDepth) { bestDepth = depth; best = id; }
        }
        return best;
    }

    private readonly Lazy<Dictionary<UnitId, Vec3[]>> _unitCorners;

    private static bool Contains(Vec2[] hull, Vec2 q)
    {
        var sign = 0;
        for (var i = 0; i < hull.Length; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            var cross = MathF.Sign((b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X));
            if (cross == 0) continue;
            if (sign != 0 && cross != sign) return false;
            sign = cross;
        }
        return hull.Length >= 3;
    }

    private static IEnumerable<Vec3> Corners(IEnumerable<SolidPart> parts) =>
        parts.SelectMany(p => p.Volume.Bottom.Select(q => new Vec3(q.X, p.Volume.Y0, q.Z))
            .Concat(p.Volume.Top.Select(q => new Vec3(q.X, p.Volume.Y1, q.Z))));

    /// <summary>Every solid: stand, pads, the six units and the pipework.</summary>
    public ImmutableArray<SolidPart> All { get; }

    /// <summary>The pad under each unit. Pads belong to the stand: they stay down when a unit lifts.</summary>
    public ImmutableDictionary<UnitId, SolidPart> Pads { get; }

    /// <summary>One unit's solids, standing on its pad.</summary>
    public ImmutableArray<SolidPart> UnitWithPad(UnitId id) => _unitWithPad.GetOrAdd(id, u => [Pads[u], .. All.Where(p => p.Unit == u)]);

    // One array per unit, so a renderer's shape cache (keyed on the parts list) holds across frames.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<UnitId, ImmutableArray<SolidPart>> _unitWithPad = new();

    /// <summary>Attaches line work to the part just added.</summary>
    private void With(Func<OrbitProjection, double, IEnumerable<DetailLine>> details) => _parts[^1] = _parts[^1] with { Details = details };

    /// <summary>Fan speed, radians per second (the figure's 1.6).</summary>
    public const float FanSpeed = 1.6f;

    // Builders, with the figure's defaults (slab: 4 steps, 0.5 bevel; post: 28 steps, no bevel).
    private void Slab(UnitId? unit, string name, Plan plan, float y, float height, int steps = 4, float bevel = 0.5f, EdgeTone edge = EdgeTone.Mid, bool lit = false, bool crease = true) =>
        _parts.Add(new(name, unit, Volume.Box(plan, y, height), p => SolidBuilder.Slab(plan, y, height, p, steps, bevel), edge, lit, crease));

    private void Post(UnitId? unit, string name, float x, float z, float r, float y, float height, int steps = 28, float bevel = 0, EdgeTone edge = EdgeTone.Mid, bool lit = false) =>
        _parts.Add(new(name, unit, Volume.Round(x, z, r, y, height), p => SolidBuilder.Cylinder(x, z, r, y, height, p, steps, bevel), edge, lit));

    private void TurnedUp(UnitId unit, string name, float x, float z, (float S, float R)[] profile, bool lit = true)
    {
        var axis = Frame.Vertical(x, z);
        // A two-knot profile is a cone: a tapered hull keeps its silhouette tight (the silo
        // hopper's full-width hull overlapped the front brace on screen and closed a cycle).
        var volume = profile.Length == 2
            ? Volume.Cone(x, z, profile[0].R, profile[1].R, profile[0].S, profile[1].S - profile[0].S)
            : Volume.Round(x, z, profile.Max(k => k.R), profile[0].S, profile[^1].S - profile[0].S);
        _parts.Add(new(name, unit, volume, p => SolidBuilder.Turned(profile, axis, p), Lit: lit));
    }

    private void Turned(UnitId? unit, string name, Frame axis, (float S, float R)[] profile, bool lit = false)
    {
        var r = profile.Max(k => k.R);
        _parts.Add(new(name, unit, BoxAlong(axis, profile[0].S, profile[^1].S, r), p => SolidBuilder.Turned(profile, axis, p), Lit: lit));
    }

    private void Disc(UnitId? unit, string name, Frame axis, float s0, float s1, float r) =>
        _parts.Add(new(name, unit, BoxAlong(axis, s0, s1, r), p => SolidBuilder.Disc(s0, s1, r, axis, p)));

    /// <summary>The axis-aligned box round a turned part whose axis is a world axis.</summary>
    private static Volume BoxAlong(Frame axis, float s0, float s1, float r)
    {
        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        foreach (var s in new[] { s0, s1 })
        {
            for (var k = 0; k < 4; k++)
            {
                var q = axis.PointAt(s, r, k * MathF.PI / 2);
                minX = MathF.Min(minX, q.X); maxX = MathF.Max(maxX, q.X);
                minY = MathF.Min(minY, q.Y); maxY = MathF.Max(maxY, q.Y);
                minZ = MathF.Min(minZ, q.Z); maxZ = MathF.Max(maxZ, q.Z);
            }
        }
        return Volume.Box(new(minX, minZ, maxX - minX, maxZ - minZ, 0), minY, maxY - minY);
    }

    private static PlanPoint[] Corners(Plan plan, float inset) =>
    [
        new(plan.X + inset, plan.Z + inset),
        new(plan.X + plan.W - inset, plan.Z + inset),
        new(plan.X + inset, plan.Z + plan.D - inset),
        new(plan.X + plan.W - inset, plan.Z + plan.D - inset),
    ];

    /// <summary>Base plate on feet, a ruler strip and a label plate, and a pad under each unit.</summary>
    private void Stand()
    {
        foreach (var c in Corners(Plate, 24))
        {
            Post(null, "stand.foot", c.X, c.Z, 8, -PlateHeight - FootHeight, 1.6f, edge: EdgeTone.Lo);
            Post(null, "stand.foot", c.X, c.Z, 6, -PlateHeight - FootHeight + 1.6f, FootHeight - 1.6f);
        }
        Slab(null, "stand.plate", Plate, -PlateHeight, PlateHeight, steps: 8, bevel: 1.6f, edge: EdgeTone.Lo);
        var screws = Corners(Plate, 10);
        With((p, _) => [
            Details.PlanOutline(EdgeTone.Faint, new(Plate.X + 5, Plate.Z + 5, Plate.W - 10, Plate.D - 10, Plate.R - 5), 0, p),
            .. screws.Select(c => Details.Ring(EdgeTone.Lo, c.X, c.Z, 2.2f, 0, p, 16)),
            .. Details.Dots(EdgeTone.Mid, screws.Select(c => new Vec3(c.X, 0, c.Z)), p),
        ]);
        var strip = new Plan(-140, Plate.Z + Plate.D - 13, 196, 7, 1);
        Slab(null, "stand.strip", strip, 0, 0.6f, steps: 3, bevel: 0, edge: EdgeTone.Lo, crease: false);
        With((p, _) => Details.Ticks(strip.X + 4, strip.X + strip.W - 4, 4, 5, strip.Z + 0.8f, 0.6f, 2, 3.6f, p));
        var label = new Plan(72, Plate.Z + Plate.D - 18, 60, 12, 1.2f);
        Slab(null, "stand.label", label, 0, 0.8f, bevel: 0, edge: EdgeTone.Lo);
        With((p, _) => [
            Details.PlanOutline(EdgeTone.Faint, new(label.X + 1.6f, label.Z + 1.6f, label.W - 3.2f, label.D - 3.2f, 0), 0.8f, p, 4),
            .. new[] { (4f, 38f), (6.5f, 30f), (9f, 22f) }.Select(l => Details.OnTop(EdgeTone.Lo, label.X + 5, label.Z + l.Item1, label.X + 5 + l.Item2, label.Z + l.Item1, 0.8f, p)),
        ]);

        foreach (var (id, c) in Centres)
        {
            var plan = new Plan(c.X - PadHalf, c.Z - PadHalf, PadHalf * 2, PadHalf * 2, 4);
            var bolts = Corners(plan, 4).Select(q => new Vec3(q.X, PadHeight, q.Z)).ToArray();
            var pad = new SolidPart("stand.pad", null, Volume.Box(plan, 0, PadHeight), p => SolidBuilder.Slab(plan, 0, PadHeight, p, 4, 0.4f), EdgeTone.Lo)
            {
                PadOf = id,
                Details = (p, _) => Details.Dots(EdgeTone.Mid, bolts, p),
            };
            _pads[id] = pad;
            _parts.Add(pad);
        }
    }

    /// <summary>STK-01: two stacks on a foundation, a flue duct between them, a gallery on each.</summary>
    private void Stacks()
    {
        const UnitId u = UnitId.Stk;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        Slab(u, "stk.foundation", new(cx - 24, cz - 16, 48, 32, 3), PadHeight, 4.8f, bevel: 0.6f);
        const float y0 = PadHeight + 4.8f;
        var duct = new Plan(cx - 5, cz - 5, 11, 10, 1.5f);
        Slab(u, "stk.duct", duct, y0 + 2, 10);
        With((p, _) => Details.SideSeam(EdgeTone.Faint, duct, y0 + 7, p));
        foreach (var (x, r, deck, top) in new[] { (cx - 12, 7f, 72f, 100f), (cx + 12, 6f, 58f, 80f) })
        {
            Post(u, "stk.flange", x, cz, r + 1.6f, y0, 2, bevel: 0.4f);
            With((p, _) => Details.DotRing(EdgeTone.Mid, x, cz, r + 0.9f, y0 + 2, 12, p));
            Post(u, "stk.shaft", x, cz, r, y0 + 2, deck - y0 - 2, steps: 48, lit: true);
            With((p, _) => [Details.SideArc(EdgeTone.Faint, x, cz, r, deck - 40, p, 32), Details.SideArc(EdgeTone.Faint, x, cz, r, deck - 20, p, 32)]);
            Post(u, "stk.gallery", x, cz, r + 3.6f, deck, 1.6f);
            With((p, _) => [Details.Ring(EdgeTone.Lo, x, cz, r + 3, deck + 1.6f, p), .. Details.DotRing(EdgeTone.Mid, x, cz, r + 3, deck + 1.6f, 16, p)]);
            Post(u, "stk.shaft", x, cz, r, deck + 1.6f, top - deck - 1.6f, steps: 48, lit: true);
            Post(u, "stk.lip", x, cz, r + 0.8f, top, 3);
            With((p, _) => [Details.Ring(EdgeTone.Lo, x, cz, r - 0.6f, top + 3, p)]);
        }
    }

    /// <summary>TKA-02: a bund wall round two cone-roofed tanks, steps over the wall and a pump skid.</summary>
    private void TankFarm()
    {
        const UnitId u = UnitId.Tka;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        const float t = 2.4f, wall = 6;
        foreach (var plan in new Plan[]
        {
            new(cx - 32, cz - 32, 64, t, 0.6f),
            new(cx - 32, cz - 32 + t, t, 64 - 2 * t, 0.6f),
            new(cx + 32 - t, cz - 32 + t, t, 64 - 2 * t, 0.6f),
            new(cx - 32, cz + 32 - t, 64, t, 0.6f),
        })
        {
            Slab(u, "tka.bund", plan, PadHeight, wall, steps: 2, bevel: 0.3f);
        }
        foreach (var (z0, z1, top) in new[] { (34.4f, 36f, 2f), (33.2f, 34.4f, 4f), (32f, 33.2f, wall) })
        {
            Slab(u, "tka.step", new(cx - 24, cz + z0, 6, z1 - z0, 0.2f), PadHeight, top, steps: 2, bevel: 0);
        }

        var skid = new Plan(cx + 6, cz + 16, 18, 9, 0.8f);
        Slab(u, "tka.skid", skid, PadHeight, 2, steps: 2, bevel: 0.3f);
        With((p, _) => Details.Dots(EdgeTone.Mid, [new(skid.X + 1.5f, PadHeight + 2, skid.Z + 1.5f), new(skid.X + skid.W - 1.5f, PadHeight + 2, skid.Z + 1.5f)], p));
        var pumpAxis = new Frame(new(0, PadHeight + 5, skid.Z + 4.5f), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        foreach (var (name, s0, s1, r) in new[] { ("tka.motor", cx + 7.5f, cx + 15, 3f), ("tka.coupling", cx + 15, cx + 16.5f, 1.4f), ("tka.pump", cx + 16.5f, cx + 22, 3f) })
        {
            Disc(u, name, pumpAxis, s0, s1, r);
        }

        foreach (var (x, z) in new[] { (cx + 12, cz - 10), (cx - 12, cz + 10) })
        {
            const float top = 30;
            Post(u, "tka.tank", x, z, 13, PadHeight, top - PadHeight, steps: 56, bevel: 0.6f, lit: true);
            With((p, _) => [Details.SideArc(EdgeTone.Faint, x, z, 13, 11, p, 40), Details.SideArc(EdgeTone.Faint, x, z, 13, 21, p, 40)]);
            TurnedUp(u, "tka.roof", x, z, [(top, 13), (top + 7, 2.5f)]);
            Post(u, "tka.vent", x, z, 1.6f, top + 7, 3);
            Post(u, "tka.cap", x, z, 2.4f, top + 10, 1.2f);
        }
    }

    /// <summary>GEN-03: a gabled hall with a roller door and louvres, and a transformer outside.</summary>
    private void GeneratorHall()
    {
        const UnitId u = UnitId.Gen;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        var hall = new Plan(cx - 30, cz - 24, 60, 36, 1.5f);
        Slab(u, "gen.hall", hall, PadHeight, 30 - PadHeight, bevel: 0.6f, lit: true);
        With((p, _) => [.. Details.SideSeam(EdgeTone.Faint, hall, 10, p), .. Details.SideSeam(EdgeTone.Faint, hall, 20, p)]);

        var ridge = new Frame(new(cx - 31, 30, cz - 6), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        (float, float)[] gable = [(-19, 0), (19, 0), (0, 9)];
        _parts.Add(new("gen.roof", u, Volume.Box(new(cx - 31, cz - 25, 62, 38, 0), 30, 9), p => SolidBuilder.Prism(ridge, gable, 0, 62, p), Lit: true));

        var door = new Plan(cx - 22, cz + 12, 18, 1, 0.3f);
        Slab(u, "gen.door", door, PadHeight, 19, steps: 2, bevel: 0);
        With((p, _) => new[] { 4f, 7f, 10f, 13f, 16f }.SelectMany(dy => Details.SideSeam(EdgeTone.Lo, door, PadHeight + dy, p, 2)));
        foreach (var z in new[] { cz - 19, cz - 10, cz - 1 })
        {
            Slab(u, "gen.louvre", new(cx + 30, z, 0.8f, 6, 0.3f), 18, 6, steps: 2, bevel: 0);
        }

        var tx = new Plan(cx + 4, cz + 18, 20, 12, 1.5f);
        Slab(u, "gen.transformer", tx, PadHeight, 12);
        // Cooling fins on the front face, drawn only while that face is toward the viewer.
        With((p, _) => p.CosYaw <= 0 ? [] : Enumerable.Range(0, 7).Select(i =>
            Details.Segment(EdgeTone.Lo, new(tx.X + 2.5f + i * 2.5f, PadHeight, tx.Z + tx.D), new(tx.X + 2.5f + i * 2.5f, PadHeight + 12, tx.Z + tx.D), p)));
        foreach (var x in new[] { tx.X + 5, tx.X + 10, tx.X + 15 })
        {
            Post(u, "gen.bushing", x, tx.Z + 6, 1.2f, PadHeight + 12, 5, steps: 16);
            Post(u, "gen.bushing.cap", x, tx.Z + 6, 1.8f, PadHeight + 17, 1, steps: 16);
        }
        foreach (var x in new[] { tx.X + 2, tx.X + 17 })
        {
            Slab(u, "gen.bracket", new(x, tx.Z + 1, 1, 2, 0.2f), PadHeight + 12, 2, steps: 2, bevel: 0);
        }
        var conservator = new Frame(new(0, PadHeight + 15.8f, tx.Z + 2), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        Disc(u, "gen.conservator", conservator, tx.X + 1, tx.X + 19, 1.8f);
    }

    /// <summary>TRB-04: HP, IP and LP casings and the generator on one shaft, on pedestals on a deck.</summary>
    private void TurbineHouse()
    {
        const UnitId u = UnitId.Trb;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        foreach (var x in new[] { cx - 28, cx - 2, cx + 24 })
        {
            foreach (var z in new[] { cz - 10, cz + 6 })
            {
                Slab(u, "trb.column", new(x, z, 4, 4, 0.6f), PadHeight, 12 - PadHeight, steps: 2, bevel: 0.3f);
            }
        }
        var deck = new Plan(cx - 30, cz - 12, 60, 24, 2);
        Slab(u, "trb.deck", deck, 12, 4, bevel: 0.6f);
        With((p, _) => Details.SideSeam(EdgeTone.Faint, deck, 14, p));
        foreach (var (z0, z1, top) in new[] { (16.5f, 18f, 5.2f), (15f, 16.5f, 9.2f), (13.5f, 15f, 13.2f), (12f, 13.5f, 16f) })
        {
            Slab(u, "trb.step", new(cx + 8, cz + z0, 6, z1 - z0, 0.2f), PadHeight, top - PadHeight, steps: 2, bevel: 0);
        }
        var oil = new Plan(cx - 28, cz + 18, 14, 10, 1.2f);
        Slab(u, "trb.oil", oil, PadHeight, 6, bevel: 0.4f);
        With((p, _) => Details.SideSeam(EdgeTone.Faint, oil, PadHeight + 3, p));
        Post(u, "trb.filler", oil.X + 10, oil.Z + 5, 1.4f, PadHeight + 6, 1.6f, steps: 16);

        const float shaftY = 28;
        var shaft = new Frame(new(cx, shaftY, cz), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        foreach (var (s0, s1) in new[] { (-30f, -27f), (-15.5f, -13.5f), (-0.5f, 1.5f), (18.5f, 20.5f), (28.5f, 30f) })
        {
            Slab(u, "trb.pedestal", new(cx + s0, cz - 5, s1 - s0, 10, 0.8f), 16, shaftY - 2 - 16, steps: 2, bevel: 0.3f);
        }
        foreach (var (s0, s1) in new[] { (-28f, -26f), (-16f, -13f), (-1f, 2f), (18f, 20.5f), (28.5f, 29.5f) })
        {
            Disc(u, "trb.shaft", shaft, s0, s1, 2);
        }
        Turned(u, "trb.hp", shaft, [(-26, 5), (-25, 6), (-17, 6), (-16, 5)], lit: true);
        Turned(u, "trb.ip", shaft, [(-13, 6.5f), (-12, 8), (-2, 8), (-1, 6.5f)], lit: true);
        Turned(u, "trb.lp", shaft, [(2, 9), (3.5f, 11), (16.5f, 11), (18, 9)], lit: true);
        Turned(u, "trb.gen", shaft, [(20.5f, 7.5f), (28.5f, 7.5f)], lit: true);
    }

    /// <summary>CND-05: a mechanical-draft cell on its basin with six fan stacks on top.</summary>
    private void CondenserBank()
    {
        const UnitId u = UnitId.Cnd;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        Slab(u, "cnd.basin", new(cx - 30, cz - 24, 60, 48, 2), PadHeight, 4);
        var cell = new Plan(cx - 28, cz - 22, 56, 44, 2);
        Slab(u, "cnd.cell", cell, PadHeight + 4, 25, bevel: 0.6f, lit: true);
        // Louvred air inlets low on the cell, and a faint band below the fan deck.
        With((p, _) => [
            .. new[] { 8f, 10f, 12f, 14f, 16f }.SelectMany(dy => Details.SideSeam(EdgeTone.Lo, cell, PadHeight + dy, p)),
            .. Details.SideSeam(EdgeTone.Faint, cell, PadHeight + 19, p),
        ]);
        foreach (var x in new[] { cx - 18, cx, cx + 18 })
        {
            foreach (var z in new[] { cz - 11, cz + 11 })
            {
                Post(u, "cnd.stack", x, z, 7.5f, FanTop - 6, 6, steps: 40, bevel: 0.5f, lit: true);
                // Guard ring, hub and five blades; the blades turn with time, each fan a little out of phase.
                var phase = (x + z) * 0.37f;
                With((p, t) => [
                    Details.Ring(EdgeTone.Lo, x, z, 6.3f, FanTop, p),
                    Details.Ring(EdgeTone.Mid, x, z, 1.4f, FanTop, p, 16),
                    .. Details.Blades(x, z, FanTop, phase + (float)(t * FanSpeed % (Math.PI * 2)), p),
                ]);
            }
        }
    }

    /// <summary>The top of the condenser's fan stacks, where the blades turn.</summary>
    public const float FanTop = PadHeight + 29 + 6;

    /// <summary>SIL-06: a body on four legs over a hopper and chute, a cone roof with a vent filter.</summary>
    private void AshSilo()
    {
        const UnitId u = UnitId.Sil;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        foreach (var (dx, dz) in new[] { (-10f, -10f), (10f, -10f), (-10f, 10f), (10f, 10f) })
        {
            Slab(u, "sil.leg", new(cx + dx - 1.5f, cz + dz - 1.5f, 3, 3, 0.6f), PadHeight, 26 - PadHeight, steps: 2, bevel: 0.3f);
        }
        foreach (var plan in new Plan[]
        {
            new(cx - 8.5f, cz - 11.5f, 17, 3, 0.3f),
            new(cx - 11.5f, cz - 8.5f, 3, 17, 0.3f),
            new(cx + 8.5f, cz - 8.5f, 3, 17, 0.3f),
            new(cx - 8.5f, cz + 8.5f, 17, 3, 0.3f),
        })
        {
            Slab(u, "sil.brace", plan, 12, 2, steps: 2, bevel: 0);
        }
        Post(u, "sil.chute", cx, cz, 2, 8, 6, steps: 20);
        TurnedUp(u, "sil.hopper", cx, cz, [(14, 3), (26, 10.5f)]);
        Post(u, "sil.body", cx, cz, 13, 26, 36, steps: 56, bevel: 0.6f, lit: true);
        With((p, _) => [Details.SideArc(EdgeTone.Faint, cx, cz, 13, 38, p, 40), Details.SideArc(EdgeTone.Faint, cx, cz, 13, 50, p, 40)]);
        TurnedUp(u, "sil.roof", cx, cz, [(62, 13), (68, 3)]);
        Post(u, "sil.vent", cx, cz, 2, 68, 3, steps: 16);
        Post(u, "sil.filter", cx, cz, 3.2f, 71, 5, steps: 24, bevel: 0.4f);
    }

    /// <summary>
    /// Two headers down the corridor on sleepers, and steam and fuel lines between neighbours, each
    /// run ending in a flange on a pier so a unit lifts free.
    /// </summary>
    private void Pipework()
    {
        void Pier(Plan plan, float height)
        {
            Slab(null, "stand.pier", plan, 0, height, bevel: 0.4f);
            With((p, _) => Details.Dots(EdgeTone.Mid, [new(plan.X + plan.W / 2, height, plan.Z + 1.6f), new(plan.X + plan.W / 2, height, plan.Z + plan.D - 1.6f)], p));
        }
        void Pipe(string name, Vec3 a, Vec3 b, float r)
        {
            var along = b - a;
            var length = along.Length;
            var dir = new Vec3(along.X / length, along.Y / length, along.Z / length);
            var t = MathF.Max(0.9f, r * 0.55f);
            var rim = r * 1.55f;
            Vec3 At(float s) => new(a.X + dir.X * s, a.Y + dir.Y * s, a.Z + dir.Z * s);
            var axis = Frame.Along(a, along);
            _parts.Add(new(name, null, BoxAlong(axis, t, length - t, r), p => SolidBuilder.Tube(At(t), At(length - t), r, p)));
            // A flow mark along the crown, in the direction the line runs.
            With((p, _) => [Details.Flow(At(t), At(length - t), r, p)]);
            Disc(null, name + ".flange", axis, 0, t, rim);
            Disc(null, name + ".flange", axis, length - t, length, rim);
        }

        Pier(new(-150, -8, 8, 16, 0.8f), 10);
        Pier(new(142, -8, 8, 16, 0.8f), 10);
        foreach (var x in new[] { -122f, -84f, -47.5f, -12f, 20f, 47.5f, 84f, 122f })
        {
            Slab(null, "stand.sleeper", new(x - 1.5f, -6.5f, 3, 13, 0.3f), 0, 3.3f, steps: 2, bevel: 0);
        }
        Pipe("rack.north", new(-142, 5.5f, -3), new(142, 5.5f, -3), 2.2f);
        Pipe("rack.south", new(142, 5.5f, 3), new(-142, 5.5f, 3), 2.2f); // the return header runs west

        Pier(new(37, 42, 5, 12, 0.6f), 12);
        Pier(new(53, 42, 5, 12, 0.6f), 12);
        Pipe("steam", new(42, 6.5f, 48), new(53, 6.5f, 48), 2.6f);
        Pier(new(37, -35, 5, 10, 0.6f), 9);
        Pier(new(53, -35, 5, 10, 0.6f), 9);
        Pipe("fuel", new(42, 4.8f, -30), new(53, 4.8f, -30), 1.6f);
    }
}
