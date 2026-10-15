using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>The fluid a pipe carries. Pipes are tinted by circuit so a run reads end to end.</summary>
public enum Circuit
{
    None,
    Steam,
    Water,
    Cooling,
    Fuel,
}

/// <summary>
/// The rest of the thermal cycle, drawn the way a technical isometric draws a plant room (the user's
/// reference: Andrew Kras, Isometric Swimming Pool). A boiler bay west of the units closes the cycle:
/// fuel from the tank farm feeds two burners, the drum's steam runs to a valve station by the turbine,
/// feedwater pumps return condensate to the boiler, the flue runs to the stacks and ash to the silo.
/// A step-up transformer stands beside the generator hall, and control cabinets sit at each corridor.
/// <para>
/// Every run keeps to the corridors between the unit pads and ends in a flange at a pad's edge, so a
/// selected unit lifts free and no two hulls interpenetrate (the depth order needs convex, disjoint
/// hulls; <c>PlantSolidsTests</c> checks both). Fittings are axis-aligned for the same reason: a bend
/// is a knuckle (a short cylinder round the corner) with the next run butting onto its side.
/// </para>
/// </summary>
public sealed partial class PlantSolids
{
    /// <summary>The boiler bay's pad: the stand extends west to hold it.</summary>
    private static readonly Plan BoilerPad = new(-238, -78, 82, 100, 3);

    private const float BodyTop = 70;
    private const float DrumY = 80;
    private const float DrumZ = -27;

    private void ThermalCycle()
    {
        Boiler();
        BoilerRuns();
        FeedwaterPumps();
        Transformer();
        Cabinet("cab.boiler", new(-152, 32, 10, 5, 0.6f), 14);
        Cabinet("cab.west", new(-54, -72, 9, 5, 0.6f), 14);
        Cabinet("cab.east", new(40, -75, 14, 5, 0.6f), 12);
        Cabinet("cab.yard", new(135, -30, 8, 5, 0.6f), 12);
    }

    /// <summary>
    /// A drum boiler: a furnace body with wall-tube lines on its visible faces, a windbox with two
    /// burners on the east face, a roof deck on four columns with a handrail and a ladder, and the
    /// steam drum on saddles.
    /// </summary>
    private void Boiler()
    {
        Slab(null, "stand.boilerpad", BoilerPad, 0, PadHeight, bevel: 0.4f, edge: EdgeTone.Lo);
        var body = new Plan(-228, -68, 54, 82, 2);
        Slab(null, "boiler.body", body, PadHeight, BodyTop - PadHeight, bevel: 0.6f);
        With((p, _) => [
            .. FaceLines(body, PadHeight + 6, BodyTop - 4, 4, p),
            .. Details.SideSeam(EdgeTone.Lo, body, 24, p),
            .. Details.SideSeam(EdgeTone.Lo, body, 48, p),
        ]);

        // Columns at the corners carry the roof deck; the east-front column carries the ladder.
        foreach (var (x, z) in new[] { (-231.4f, -71.4f), (-173f, -71.4f), (-231.4f, 15f), (-173f, 15f) })
        {
            Slab(null, "boiler.column", new(x, z, 2.4f, 2.4f, 0.3f), PadHeight, BodyTop - PadHeight, steps: 2, bevel: 0);
        }
        With((p, _) => Ladder(new(-170.6f, PadHeight, 15.4f), new(0, 0, 1), 1.6f, BodyTop, p));
        var deck = new Plan(-230, -70, 58, 86, 1);
        Slab(null, "boiler.deck", deck, BodyTop, 1.2f, steps: 2, bevel: 0.2f);
        With((p, _) => Handrail(deck, BodyTop + 1.2f, 4, 8, p));

        // Steam drum on two saddles, with end caps.
        foreach (var x in new[] { -218f, -186f })
        {
            // 0.1 off the deck: touching faces tie in the order at grazing elevations.
            Slab(null, "boiler.saddle", new(x, DrumZ - 4, 4, 8, 0.4f), BodyTop + 1.3f, DrumY - 6 - BodyTop - 1.3f, steps: 2, bevel: 0);
        }
        var drum = new Frame(new(0, DrumY, DrumZ), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        Disc(null, "boiler.drum.cap", drum, -224, -223, 6.6f);
        Disc(null, "boiler.drum", drum, -223, -179, 6);
        With((p, _) => [Details.Segment(EdgeTone.Lo, new(-201, DrumY + 6, DrumZ), new(-201, DrumY + 9, DrumZ), p)]);
        Disc(null, "boiler.drum.cap", drum, -179, -178, 6.6f);
        Post(null, "boiler.safety", -201, DrumZ, 1.2f, DrumY + 6, 3, steps: 16);

        // Windbox and burners on the east face.
        var windbox = new Plan(-174, -56, 3, 42, 0.4f);
        Slab(null, "boiler.windbox", windbox, 16, 16, steps: 2, bevel: 0.2f);
        foreach (var z in new[] { -50f, -20f })
        {
            var axis = new Frame(new(0, 24, z), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
            Disc(null, "boiler.burner.ring", axis, -171, -170, 4.5f);
            Disc(null, "boiler.burner", axis, -170, -166, 3);
        }

        // Flue duct from the furnace top to the stacks' pad, on two legs, ending in a collar.
        var duct = new Plan(-174, -53, 39, 10, 0.5f);
        Slab(null, "flue.duct", duct, 56, 10, steps: 2, bevel: 0.3f);
        With((p, _) => Details.SideSeam(EdgeTone.Faint, duct, 61, p));
        Slab(null, "flue.collar", new(-135, -54.5f, 2, 13, 0.3f), 54.5f, 13, steps: 2, bevel: 0);
        foreach (var x in new[] { -151f, -140f })
        {
            Slab(null, "flue.leg", new(x, -50, 2, 4, 0.2f), 0, 56, steps: 2, bevel: 0);
        }
    }

    /// <summary>Fuel, steam, feedwater and ash runs, with their supports and the main steam valve.</summary>
    private void BoilerRuns()
    {
        // Fuel from the tank farm along the north margin, a tee to each burner.
        Support(new(-14.2f, -95.2f, 2.4f, 2.4f, 0.2f), 3.4f);
        Run("fuel.main", Circuit.Fuel, 1.6f, [new(-8, 5, -94), new(-158, 5, -94), new(-158, 5, -50), new(-158, 5, -20), new(-158, 24, -20), new(-166, 24, -20)]);
        Run("fuel.branch", Circuit.Fuel, 1.6f, [new(-158, 5, -50), new(-158, 24, -50), new(-166, 24, -50)], start: End.Knuckle);
        foreach (var x in new[] { -60f, -110f })
        {
            Support(new(x - 1.2f, -95.2f, 2.4f, 2.4f, 0.2f), 3.4f);
        }

        // Main steam: off the drum, down the boiler's east side, along the corridor's north lane and
        // south between the silo and the turbine to the stop valve, then a stub to the turbine's pad.
        Run("steam.main", Circuit.Steam, 2.4f, [new(-178, DrumY, DrumZ), new(-164, DrumY, DrumZ), new(-164, 18, DrumZ), new(-164, 18, -9.5f), new(-48, 18, -9.5f), new(-48, 18, 28)]);
        Valve("steam.valve", new(-48, 18, 32), along: new(0, 0, 1), 2.4f);
        Run("steam.turbine", Circuit.Steam, 2.4f, [new(-48, 18, 36), new(-48, 18, 48), new(-38, 18, 48)]);
        foreach (var x in new[] { -140f, -100f })
        {
            Support(new(x - 1.2f, -10.7f, 2.4f, 2.4f, 0.2f), 15.6f);
        }
        Support(new(-49.2f, 13.8f, 2.4f, 2.4f, 0.2f), 15.6f);
        Support(new(-49.2f, 41.8f, 2.4f, 2.4f, 0.2f), 15.6f);

        // Ash from the furnace floor to the silo's pad.
        Run("ash", Circuit.None, 1.8f, [new(-200, 6, 14), new(-200, 6, 30), new(-133, 6, 30)]);
        Support(new(-161.2f, 28.8f, 2.4f, 2.4f, 0.2f), 4.2f);
    }

    /// <summary>Two feedwater pumps in the east corridor; their discharge joins one main back to the boiler.</summary>
    private void FeedwaterPumps()
    {
        foreach (var x in new[] { 43f, 52f })
        {
            PumpSet("fw.pump", x, 17, 31);
        }
        Run("fw.main", Circuit.Water, 2, [new(52, 8.1f, 28.5f), new(52, 12.5f, 28.5f), new(52, 12.5f, 9.5f), new(43, 12.5f, 9.5f), new(-174, 12.5f, 9.5f)]);
        Run("fw.branch", Circuit.Water, 2, [new(43, 8.1f, 28.5f), new(43, 12.5f, 28.5f), new(43, 12.5f, 9.5f)], end: End.Knuckle);
        foreach (var x in new[] { 0f, -100f })
        {
            Support(new(x - 1.2f, 8.3f, 2.4f, 2.4f, 0.2f), 10.5f);
        }
    }

    /// <summary>The step-up transformer beside the generator hall, with radiators, bushings and a conservator.</summary>
    private void Transformer()
    {
        Slab(null, "stand.yard", new(134, -68, 22, 32, 1.5f), 0, PadHeight, bevel: 0.3f, edge: EdgeTone.Lo);
        var tank = new Plan(138, -62, 14, 22, 1);
        Slab(null, "xfmr.tank", tank, PadHeight, 14, bevel: 0.4f);
        With((p, _) => FaceLines(tank, PadHeight + 2, PadHeight + 12, 2, p));
        foreach (var z in new[] { -60f, -51f })
        {
            Slab(null, "xfmr.radiator", new(152, z, 2.5f, 7, 0.2f), PadHeight + 1, 11, steps: 2, bevel: 0);
        }
        foreach (var x in new[] { 141f, 145f, 149f })
        {
            Post(null, "xfmr.bushing", x, -45, 1.1f, PadHeight + 14, 6, steps: 16);
            Post(null, "xfmr.bushing.cap", x, -45, 1.7f, PadHeight + 20, 1, steps: 16);
        }
        foreach (var x in new[] { 139f, 150f })
        {
            Slab(null, "xfmr.bracket", new(x, -59, 1, 2, 0.2f), PadHeight + 14, 4, steps: 2, bevel: 0);
        }
        var conservator = new Frame(new(0, PadHeight + 20, -58), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        Disc(null, "xfmr.conservator", conservator, 138, 152, 2);
        // A bus duct from the generator hall's pad edge.
        Slab(null, "xfmr.busduct", new(131.5f, -48, 6.5f, 4, 0.3f), 10, 3, steps: 2, bevel: 0);
        Slab(null, "xfmr.busduct.leg", new(132, -47, 2, 2, 0.2f), 0, 10, steps: 2, bevel: 0);
    }

    // ---------------------------------------------------------------- kit

    private enum End { Flange, Knuckle }

    /// <summary>
    /// A pipe run through axis-aligned points: a tube per leg, a knuckle at every interior point (a
    /// bend, or a tee when the next point is straight on), flanges at the ends. A run that starts or
    /// ends on another run's knuckle passes <see cref="End.Knuckle"/>: it stops at the knuckle's side.
    /// </summary>
    private void Run(string name, Circuit circuit, float r, Vec3[] points, End start = End.Flange, End end = End.Flange)
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
            var s0 = i == 0 ? (start == End.Flange ? t : k) : k;
            var s1 = i == points.Length - 2 ? (end == End.Flange ? length - t : length - k) : length - k;
            if (i == 0 && start == End.Flange) AddPipePart(PipeDisc(name + ".flange", axis, 0, t, rim), circuit);
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
                AddPipePart(new(name, null, BoxAlong(axis, p0, p1, r), p => SolidBuilder.Tube(from, to, r, p, PipeFacets)), circuit);
                With((p, _) => [Details.Flow(from, to, r, p)]);
            }
            if (i == points.Length - 2 && end == End.Flange) AddPipePart(PipeDisc(name + ".flange", axis, length - t, length, rim), circuit);
            if (i < points.Length - 2) AddPipePart(PipeDisc(name + ".knuckle", axis, length - k, length + k, k), circuit);
        }
    }

    /// <summary>Longest single pipe piece, in world units.</summary>
    private const float MaxPiece = 40;

    private static SolidPart PipeDisc(string name, Frame axis, float s0, float s1, float r) =>
        new(name, null, BoxAlong(axis, s0, s1, r), p => SolidBuilder.Disc(s0, s1, r, axis, p, PipeFacets));

    /// <summary>Fewest facets for pipe parts (the kit's 32 is for vessels a hundred pixels across).</summary>
    private const int PipeFacets = 12;

    private void AddPipePart(SolidPart part, Circuit circuit) => _parts.Add(part with { Circuit = circuit });

    /// <summary>A pipe support: a short post from the plate to the underside of the pipe.</summary>
    private void Support(Plan plan, float top) => Slab(null, "pipe.support", plan, 0, top, steps: 2, bevel: 0.2f, edge: EdgeTone.Lo);

    /// <summary>
    /// A gate valve set in a gap of a run: a body as wide as the pipe's flanges, a bonnet stem and a
    /// handwheel with four spokes. <paramref name="at"/> is the body's centre on the pipe's axis.
    /// </summary>
    private void Valve(string name, Vec3 at, Vec3 along, float r)
    {
        var half = r * 1.6f;
        var w = r * 1.5f;
        var plan = along.X != 0
            ? new Plan(at.X - half, at.Z - w, half * 2, w * 2, 0.6f)
            : new Plan(at.X - w, at.Z - half, w * 2, half * 2, 0.6f);
        Slab(null, name, plan, at.Y - w, w * 2, bevel: 0.4f);
        Post(null, name + ".stem", at.X, at.Z, r * 0.35f, at.Y + w, r * 2.4f, steps: 12);
        var wheelY = at.Y + w + r * 2.4f;
        var wheel = r * 2;
        Post(null, name + ".wheel", at.X, at.Z, wheel, wheelY, 0.5f, steps: 32);
        With((p, _) => [
            Details.Ring(EdgeTone.Mid, at.X, at.Z, wheel * 0.7f, wheelY + 0.5f, p, 24),
            .. Enumerable.Range(0, 4).Select(i =>
            {
                var a = i * MathF.PI / 2 + 0.4f;
                return Details.OnTop(EdgeTone.Lo, at.X, at.Z, at.X + MathF.Cos(a) * wheel * 0.7f, at.Z + MathF.Sin(a) * wheel * 0.7f, wheelY + 0.5f, p);
            }),
        ]);
    }

    /// <summary>A horizontal pump set along Z on a plinth: motor with cooling ribs, coupling guard, pump.</summary>
    private void PumpSet(string name, float x, float z0, float z1)
    {
        Slab(null, name + ".plinth", new(x - 3.5f, z0, 7, z1 - z0, 0.6f), 0, 2, steps: 2, bevel: 0.3f);
        const float y = 5.05f;
        var axis = new Frame(new(x, y, 0), new(0, 0, 1), new(1, 0, 0), new(0, 1, 0));
        Disc(null, name + ".motor", axis, z0 + 1, z0 + 8, 2.6f);
        With((p, _) => Enumerable.Range(0, 3).Select(i =>
        {
            var z = z0 + 2.5f + i * 2;
            return Details.Segment(EdgeTone.Lo, new(x, y + 2.6f, z), new(x, y + 2.6f, z + 1), p);
        }));
        Disc(null, name + ".coupling", axis, z0 + 8, z0 + 9.4f, 1.3f);
        Disc(null, name + ".pump", axis, z0 + 9.4f, z0 + 13, 3);
    }

    /// <summary>A control cabinet on a plinth: a door seam, a display and a handle on its front (+Z) face.</summary>
    private void Cabinet(string name, Plan plan, float height)
    {
        Slab(null, name + ".plinth", plan with { X = plan.X - 0.5f, Z = plan.Z - 0.5f, W = plan.W + 1, D = plan.D + 1 }, 0, 1, steps: 2, bevel: 0.2f, edge: EdgeTone.Lo);
        Slab(null, name, plan, 1, height, bevel: 0.3f);
        var front = plan.Z + plan.D;
        var mid = plan.X + plan.W / 2;
        With((p, _) => !Faces(0, 1, p) ? [] :
        [
            Details.Segment(EdgeTone.Lo, new(mid, 2, front), new(mid, height - 0.5f, front), p),
            new(EdgeTone.Mid, [
                p.Project(new(plan.X + 1.2f, height - 2.5f, front)), p.Project(new(mid - 1.2f, height - 2.5f, front)),
                p.Project(new(mid - 1.2f, height - 5.5f, front)), p.Project(new(plan.X + 1.2f, height - 5.5f, front)),
                p.Project(new(plan.X + 1.2f, height - 2.5f, front)),
            ]),
            Details.Segment(EdgeTone.Mid, new(mid + 1.2f, height / 2, front), new(mid + 1.2f, height / 2 + 2, front), p),
            .. Details.Dots(EdgeTone.Mid, [new(mid + 2.8f, height - 3, front), new(mid + 4.2f, height - 3, front)], p),
        ]);
    }

    /// <summary>True when a face with plan normal (nx, nz) faces the viewer.</summary>
    private static bool Faces(float nx, float nz, OrbitProjection p) => nx * p.SinYaw + nz * p.CosYaw > 1e-5f;

    /// <summary>Vertical lines every <paramref name="pitch"/> across a slab's visible sides (boiler wall tubes, tank fins).</summary>
    private static IEnumerable<DetailLine> FaceLines(Plan plan, float y0, float y1, float pitch, OrbitProjection p)
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
    private static IEnumerable<DetailLine> Ladder(Vec3 foot, Vec3 across, float width, float top, OrbitProjection p)
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
    private static IEnumerable<DetailLine> Handrail(Plan deck, float y, float height, float pitch, OrbitProjection p)
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
