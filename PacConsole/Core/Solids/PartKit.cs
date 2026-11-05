using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>
/// The plant's parts as they are built, in drawing order, and the kit's primitives that add them: slabs, posts, turned
/// and disc parts, pipe runs and supports, with the figure's defaults (slab: 4 steps, 0.5 bevel; post: 28 steps, no
/// bevel). <see cref="UnitSolids"/> and <see cref="CycleSolids"/> build the plant with it; <see cref="PlantSolids"/>
/// keeps the result.
/// </summary>
internal sealed class PartKit
{
    /// <summary>Longest single pipe piece, in world units.</summary>
    private const float MaxPiece = 40;

    /// <summary>Fewest facets for pipe parts (the kit's 32 is for vessels a hundred pixels across).</summary>
    private const int PipeFacets = 12;

    public ImmutableArray<SolidPart>.Builder Parts { get; } = ImmutableArray.CreateBuilder<SolidPart>();

    /// <summary>The pad under each unit, also in <see cref="Parts"/>.</summary>
    public Dictionary<UnitId, SolidPart> Pads { get; } = [];

    public void Add(SolidPart part) => Parts.Add(part);

    /// <summary>Attaches line work to the part just added.</summary>
    public void With(Func<OrbitProjection, double, IEnumerable<DetailLine>> details, bool animated = false) =>
        Parts[^1] = Parts[^1] with { Details = details, AnimatedDetails = animated };

    public void Slab(UnitId? unit, string name, Plan plan, float y, float height, int steps = 4, float bevel = 0.5f, EdgeTone edge = EdgeTone.Mid, bool lit = false, bool crease = true) =>
        Parts.Add(new(name, unit, Volume.Box(plan, y, height), p => SolidBuilder.Slab(plan, y, height, p, steps, bevel), edge, lit, crease));

    public void Post(UnitId? unit, string name, float x, float z, float r, float y, float height, int steps = 28, float bevel = 0, EdgeTone edge = EdgeTone.Mid, bool lit = false) =>
        Parts.Add(new(name, unit, Volume.Round(x, z, r, y, height), p => SolidBuilder.Cylinder(x, z, r, y, height, p, steps, bevel), edge, lit));

    public void TurnedUp(UnitId unit, string name, float x, float z, (float S, float R)[] profile, bool lit = true)
    {
        var axis = Frame.Vertical(x, z);
        // A two-knot profile is a cone: a tapered hull keeps its silhouette tight (the silo
        // hopper's full-width hull overlapped the front brace on screen and closed a cycle).
        var volume = profile.Length == 2
            ? Volume.Cone(x, z, profile[0].R, profile[1].R, profile[0].S, profile[1].S - profile[0].S)
            : Volume.Round(x, z, profile.Max(k => k.R), profile[0].S, profile[^1].S - profile[0].S);
        Parts.Add(new(name, unit, volume, p => SolidBuilder.Turned(profile, axis, p), Lit: lit));
    }

    public void Turned(UnitId? unit, string name, Frame axis, (float S, float R)[] profile, bool lit = false)
    {
        var r = profile.Max(k => k.R);
        Parts.Add(new(name, unit, BoxAlong(axis, profile[0].S, profile[^1].S, r), p => SolidBuilder.Turned(profile, axis, p), Lit: lit));
    }

    public void Disc(UnitId? unit, string name, Frame axis, float s0, float s1, float r) =>
        Parts.Add(new(name, unit, BoxAlong(axis, s0, s1, r), p => SolidBuilder.Disc(s0, s1, r, axis, p)));

    /// <summary>A pipe support: a short post from the plate to the underside of the pipe.</summary>
    public void Support(Plan plan, float top) => Slab(null, "pipe.support", plan, 0, top, steps: 2, bevel: 0.2f, edge: EdgeTone.Lo);

    /// <summary>
    /// A pipe run through axis-aligned points: a tube per leg, a knuckle at every interior point (a
    /// bend, or a tee when the next point is straight on), flanges at the ends. A run that starts or
    /// ends on another run's knuckle passes <see cref="PipeEnd.Knuckle"/>: it stops at the knuckle's side.
    /// </summary>
    public void Run(string name, Circuit circuit, float r, Vec3[] points, PipeEnd start = PipeEnd.Flange, PipeEnd end = PipeEnd.Flange)
    {
        var k = r * 1.4f;
        var rim = r * 1.55f;
        var t = MathF.Max(0.9f, r * 0.55f);
        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            var along = b - a;
            var length = along.Length;
            var axis = Frame.Along(a, along);
            var s0 = i == 0 ? (start == PipeEnd.Flange ? t : k) : k;
            var s1 = i == points.Length - 2 ? (end == PipeEnd.Flange ? length - t : length - k) : length - k;
            if (i == 0 && start == PipeEnd.Flange) AddPipe(PipeDisc(name + ".flange", axis, 0, t, rim), circuit);
            var dir = new Vec3(along.X / length, along.Y / length, along.Z / length);
            Vec3 At(float s) => new(a.X + dir.X * s, a.Y + dir.Y * s, a.Z + dir.Z * s);
            // Long legs go in butting pieces: one hull spanning the plant can close an occlusion cycle
            // with shorter parts at grazing elevations (fw.main, steam.main, fuel.main and the boiler
            // deck did), even though every hull is convex and disjoint.
            var pieces = (int)MathF.Ceiling((s1 - s0) / MaxPiece);
            for (var piece = 0; piece < pieces; piece++)
            {
                var (p0, p1) = (s0 + (s1 - s0) * piece / pieces, s0 + (s1 - s0) * (piece + 1) / pieces);
                var (from, to) = (At(p0), At(p1));
                AddPipe(new(name, null, BoxAlong(axis, p0, p1, r), p => SolidBuilder.Tube(from, to, r, p, PipeFacets)), circuit);
                With((p, _) => [Details.Flow(from, to, r, p)]);
            }
            if (i == points.Length - 2 && end == PipeEnd.Flange) AddPipe(PipeDisc(name + ".flange", axis, length - t, length, rim), circuit);
            if (i < points.Length - 2) AddPipe(PipeDisc(name + ".knuckle", axis, length - k, length + k, k), circuit);
        }
    }

    /// <summary>A pipe's flange or knuckle: a short disc along the pipe's axis.</summary>
    public static SolidPart PipeDisc(string name, Frame axis, float s0, float s1, float r) =>
        new(name, null, BoxAlong(axis, s0, s1, r), p => SolidBuilder.Disc(s0, s1, r, axis, p, PipeFacets));

    public void AddPipe(SolidPart part, Circuit circuit) => Parts.Add(part with { Circuit = circuit });

    /// <summary>The axis-aligned box round a turned part whose axis is a world axis.</summary>
    public static Volume BoxAlong(Frame axis, float s0, float s1, float r)
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

    /// <summary>The four corners of a plan, <paramref name="inset"/> in from its edges.</summary>
    public static PlanPoint[] Corners(Plan plan, float inset) =>
    [
        new(plan.X + inset, plan.Z + inset),
        new(plan.X + plan.W - inset, plan.Z + inset),
        new(plan.X + inset, plan.Z + plan.D - inset),
        new(plan.X + plan.W - inset, plan.Z + plan.D - inset),
    ];
}

/// <summary>How a pipe run ends: in a flange, or against another run's knuckle.</summary>
internal enum PipeEnd
{
    Flange,
    Knuckle,
}
