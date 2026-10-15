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
public sealed record SolidPart(string Name, UnitId? Unit, Volume Volume, Func<OrbitProjection, SolidShape> Build, EdgeTone Edge = EdgeTone.Mid, bool Lit = false, bool Crease = true);

/// <summary>
/// The plant as solids, from the reference figure's <c>docs/anatomy/plant/build.mjs</c>. The figure
/// is Z-up; here the figure's (x, y, z) is (X, Z, Y). Phase-1 spike: TKA-02 only.
/// </summary>
public static class PlantSolids
{
    public const float PadHeight = 1.2f;

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

    /// <summary>TKA-02, the fuel tank farm: a bund wall round two cone-roofed tanks, steps and a pump skid.</summary>
    public static ImmutableArray<SolidPart> Tka()
    {
        var parts = ImmutableArray.CreateBuilder<SolidPart>();
        var (cx, cz) = (Centres[UnitId.Tka].X, Centres[UnitId.Tka].Z);
        const UnitId unit = UnitId.Tka;
        const float h = PadHeight;

        void Slab(string name, Plan plan, float y, float height, int steps = 4, float bevel = 0.5f, EdgeTone edge = EdgeTone.Mid, bool lit = false, UnitId? owner = unit) =>
            parts.Add(new(name, owner, Volume.Box(plan, y, height), p => SolidBuilder.Slab(plan, y, height, p, steps, bevel), edge, lit));
        void Post(string name, float x, float z, float r, float y, float height, int steps = 28, float bevel = 0, bool lit = false) =>
            parts.Add(new(name, unit, Volume.Round(x, z, r, y, height), p => SolidBuilder.Cylinder(x, z, r, y, height, p, steps, bevel), EdgeTone.Mid, lit));

        Slab("stand.pad", new(cx - 36, cz - 36, 72, 72, 4), 0, h, bevel: 0.4f, edge: EdgeTone.Lo, owner: null);

        const float t = 2.4f, wall = 6;
        foreach (var plan in new Plan[]
        {
            new(cx - 32, cz - 32, 64, t, 0.6f),
            new(cx - 32, cz - 32 + t, t, 64 - 2 * t, 0.6f),
            new(cx + 32 - t, cz - 32 + t, t, 64 - 2 * t, 0.6f),
            new(cx - 32, cz + 32 - t, 64, t, 0.6f),
        })
        {
            Slab("tka.bund", plan, h, wall, steps: 2, bevel: 0.3f);
        }

        foreach (var (z0, z1, top) in new[] { (34.4f, 36f, 2f), (33.2f, 34.4f, 4f), (32f, 33.2f, wall) })
        {
            Slab("tka.step", new(cx - 24, cz + z0, 6, z1 - z0, 0.2f), h, top, steps: 2, bevel: 0);
        }

        var skid = new Plan(cx + 6, cz + 16, 18, 9, 0.8f);
        Slab("tka.skid", skid, h, 2, steps: 2, bevel: 0.3f);
        var pumpAxis = new Frame(new(0, h + 5, skid.Z + 4.5f), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        foreach (var (name, s0, s1, r) in new[] { ("tka.motor", cx + 7.5f, cx + 15, 3f), ("tka.coupling", cx + 15, cx + 16.5f, 1.4f), ("tka.pump", cx + 16.5f, cx + 22, 3f) })
        {
            parts.Add(new(name, unit, Volume.Box(new(s0, skid.Z + 4.5f - r, s1 - s0, 2 * r, 0), h + 5 - r, 2 * r), p => SolidBuilder.Disc(s0, s1, r, pumpAxis, p)));
        }

        foreach (var (x, z) in new[] { (cx + 12, cz - 10), (cx - 12, cz + 10) })
        {
            const float top = 30;
            Post("tka.tank", x, z, 13, h, top - h, steps: 56, bevel: 0.6f, lit: true);
            var axis = Frame.Vertical(x, z);
            parts.Add(new("tka.roof", unit, Volume.Round(x, z, 13, top, 7), p => SolidBuilder.Turned([(top, 13), (top + 7, 2.5f)], axis, p), Lit: true));
            Post("tka.vent", x, z, 1.6f, top + 7, 3);
            Post("tka.cap", x, z, 2.4f, top + 10, 1.2f);
        }

        return parts.ToImmutable();
    }
}
