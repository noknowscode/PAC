using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

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
internal sealed class CycleSolids(PartKit kit)
{
    /// <summary>The boiler bay's pad: the stand extends west to hold it.</summary>
    private static readonly Plan BoilerPad = new(-238, -78, 82, 100, 3);

    private const float PadHeight = PlantSolids.PadHeight;
    private const float BodyTop = 70;
    private const float DrumY = 80;
    private const float DrumZ = -27;

    private static ImmutableDictionary<UnitId, PlanPoint> Centres => PlantSolids.Centres;

    public void Build()
    {
        Boiler();
        BoilerRuns();
        Condenser();
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
        kit.Slab(null, "stand.boilerpad", BoilerPad, 0, PadHeight, bevel: 0.4f, edge: EdgeTone.Lo);
        var body = new Plan(-228, -68, 54, 82, 2);
        kit.Slab(null, "boiler.body", body, PadHeight, BodyTop - PadHeight, bevel: 0.6f);
        kit.With((p, _) => [
            .. Details.FaceLines(body, PadHeight + 6, BodyTop - 4, 4, p),
            .. Details.SideSeam(EdgeTone.Lo, body, 24, p),
            .. Details.SideSeam(EdgeTone.Lo, body, 48, p),
        ]);

        // Columns at the corners carry the roof deck; the east-front column carries the ladder.
        foreach (var (x, z) in new[] { (-231.4f, -71.4f), (-173f, -71.4f), (-231.4f, 15f), (-173f, 15f) })
        {
            kit.Slab(null, "boiler.column", new(x, z, 2.4f, 2.4f, 0.3f), PadHeight, BodyTop - PadHeight, steps: 2, bevel: 0);
        }
        kit.With((p, _) => Details.Ladder(new(-170.6f, PadHeight, 15.4f), new(0, 0, 1), 1.6f, BodyTop, p));
        var deck = new Plan(-230, -70, 58, 86, 1);
        kit.Slab(null, "boiler.deck", deck, BodyTop, 1.2f, steps: 2, bevel: 0.2f);
        kit.With((p, _) => Details.Handrail(deck, BodyTop + 1.2f, 4, 8, p));

        // Steam drum on two saddles, with end caps.
        foreach (var x in new[] { -218f, -186f })
        {
            // 0.1 off the deck: touching faces tie in the order at grazing elevations.
            kit.Slab(null, "boiler.saddle", new(x, DrumZ - 4, 4, 8, 0.4f), BodyTop + 1.3f, DrumY - 6 - BodyTop - 1.3f, steps: 2, bevel: 0);
        }
        var drum = new Frame(new(0, DrumY, DrumZ), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        kit.Disc(null, "boiler.drum.cap", drum, -224, -223, 6.6f);
        kit.Disc(null, "boiler.drum", drum, -223, -179, 6);
        kit.With((p, _) => [Details.Segment(EdgeTone.Lo, new(-201, DrumY + 6, DrumZ), new(-201, DrumY + 9, DrumZ), p)]);
        kit.Disc(null, "boiler.drum.cap", drum, -179, -178, 6.6f);
        kit.Post(null, "boiler.safety", -201, DrumZ, 1.2f, DrumY + 6, 3, steps: 16);

        // Windbox and burners on the east face.
        var windbox = new Plan(-174, -56, 3, 42, 0.4f);
        kit.Slab(null, "boiler.windbox", windbox, 16, 16, steps: 2, bevel: 0.2f);
        foreach (var z in new[] { -50f, -20f })
        {
            var axis = new Frame(new(0, 24, z), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
            kit.Disc(null, "boiler.burner.ring", axis, -171, -170, 4.5f);
            kit.Disc(null, "boiler.burner", axis, -170, -166, 3);
        }

        // Flue duct from the furnace top to the stacks' pad, on two legs, ending in a collar.
        var duct = new Plan(-174, -53, 39, 10, 0.5f);
        kit.Slab(null, "flue.duct", duct, 56, 10, steps: 2, bevel: 0.3f);
        kit.With((p, _) => Details.SideSeam(EdgeTone.Faint, duct, 61, p));
        kit.Slab(null, "flue.collar", new(-135, -54.5f, 2, 13, 0.3f), 54.5f, 13, steps: 2, bevel: 0);
        foreach (var x in new[] { -151f, -140f })
        {
            kit.Slab(null, "flue.leg", new(x, -50, 2, 4, 0.2f), 0, 56, steps: 2, bevel: 0);
        }
    }

    /// <summary>Fuel, steam, feedwater and ash runs, with their supports and the main steam valve.</summary>
    private void BoilerRuns()
    {
        // Fuel from the tank farm along the north margin, a tee to each burner.
        kit.Support(new(-14.2f, -95.2f, 2.4f, 2.4f, 0.2f), 3.4f);
        kit.Run("fuel.main", Circuit.Fuel, 1.6f, [new(-8, 5, -94), new(-158, 5, -94), new(-158, 5, -50), new(-158, 5, -20), new(-158, 24, -20), new(-166, 24, -20)]);
        kit.Run("fuel.branch", Circuit.Fuel, 1.6f, [new(-158, 5, -50), new(-158, 24, -50), new(-166, 24, -50)], start: PipeEnd.Knuckle);
        foreach (var x in new[] { -60f, -110f })
        {
            kit.Support(new(x - 1.2f, -95.2f, 2.4f, 2.4f, 0.2f), 3.4f);
        }

        // Main steam: off the drum, down the boiler's east side, along the corridor's north lane and
        // south between the silo and the turbine to the stop valve, then a stub to the turbine's pad.
        kit.Run("steam.main", Circuit.Steam, 2.4f, [new(-178, DrumY, DrumZ), new(-164, DrumY, DrumZ), new(-164, 18, DrumZ), new(-164, 18, -9.5f), new(-48, 18, -9.5f), new(-48, 18, 28)]);
        Valve("steam.valve", new(-48, 18, 32), along: new(0, 0, 1), 2.4f);
        kit.Run("steam.turbine", Circuit.Steam, 2.4f, [new(-48, 18, 36), new(-48, 18, 48), new(-38, 18, 48)]);
        foreach (var x in new[] { -140f, -100f })
        {
            kit.Support(new(x - 1.2f, -10.7f, 2.4f, 2.4f, 0.2f), 15.6f);
        }
        kit.Support(new(-49.2f, 13.8f, 2.4f, 2.4f, 0.2f), 15.6f);
        kit.Support(new(-49.2f, 41.8f, 2.4f, 2.4f, 0.2f), 15.6f);

        // Ash from the furnace floor to the silo's pad.
        kit.Run("ash", Circuit.None, 1.8f, [new(-200, 6, 14), new(-200, 6, 30), new(-133, 6, 30)]);
        kit.Support(new(-161.2f, 28.8f, 2.4f, 2.4f, 0.2f), 4.2f);
    }

    /// <summary>
    /// CND-07: a horizontal shell on two saddles with water boxes at both ends, the exhaust neck on top,
    /// a hotwell drum and a condensate pump on its pad. Exhaust steam arrives from the turbine along the
    /// south margin; two cooling-water stubs point to the headers.
    /// </summary>
    private void Condenser()
    {
        const UnitId u = UnitId.Cnd;
        var (cx, cz) = (Centres[u].X, Centres[u].Z);
        foreach (var dx in new[] { -16f, 12f })
        {
            kit.Slab(u, "cnd.saddle", new(cx + dx, cz - 7, 4, 14, 0.4f), PadHeight, 3, steps: 2, bevel: 0.2f);
        }
        const float y = PadHeight + 3 + 9;
        var shell = new Frame(new(0, y, cz), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        kit.Add(new("cnd.shell", u, PartKit.BoxAlong(shell, cx - 18, cx + 18, 9), p => SolidBuilder.Disc(cx - 18, cx + 18, 9, shell, p), Lit: true));
        kit.Add(new("cnd.waterbox", u, PartKit.BoxAlong(shell, cx - 21, cx - 18, 7.5f), p => SolidBuilder.Disc(cx - 21, cx - 18, 7.5f, shell, p), Lit: true));
        kit.Add(new("cnd.waterbox", u, PartKit.BoxAlong(shell, cx + 18, cx + 21, 7.5f), p => SolidBuilder.Disc(cx + 18, cx + 21, 7.5f, shell, p), Lit: true));
        kit.Slab(u, "cnd.neck", new(cx - 7, cz - 6, 14, 12, 1), y + 9, 7, bevel: 0.4f, lit: true);
        kit.With((p, _) => Details.SideSeam(EdgeTone.Lo, new(cx - 7, cz - 6, 14, 12, 1), y + 13, p));
        kit.Post(u, "cnd.hotwell", cx - 8, cz + 18, 4, PadHeight, 10, steps: 32, bevel: 0.3f);
        kit.Slab(u, "cnd.pump.plinth", new(cx + 4, cz + 14, 12, 8, 0.4f), PadHeight, 1.5f, steps: 2, bevel: 0.2f);
        var pump = new Frame(new(0, PadHeight + 1.5f + 3, cz + 18), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        kit.Disc(u, "cnd.pump.motor", pump, cx + 5, cx + 10, 2.6f);
        kit.Disc(u, "cnd.pump", pump, cx + 10.6f, cx + 15, 3);

        // Exhaust steam from the turbine's pad, west along the south margin, landing on the condenser's
        // pad clear of the pump (the last leg must outlast a knuckle plus a flange).
        kit.Run("cnd.exhaust", Circuit.Steam, 3, [new(-20, 8, 86.5f), new(-20, 8, 93), new(-176, 8, 93), new(-176, 8, 84)]);
        foreach (var x in new[] { -60f, -100f, -140f })
        {
            kit.Support(new(x - 1.2f, 91.8f, 2.4f, 2.4f, 0.2f), 5);
        }
        // Cooling water in and out, toward the headers.
        foreach (var x in new[] { -215f, -209f })
        {
            kit.Run("cnd.cw", Circuit.Cooling, 1.8f, [new(x, 6, 38.5f), new(x, 6, 33.5f)]);
        }
    }

    /// <summary>Two feedwater pumps in the east corridor; their discharge joins one main back to the boiler.</summary>
    private void FeedwaterPumps()
    {
        foreach (var x in new[] { 43f, 52f })
        {
            PumpSet("fw.pump", x, 17, 31);
        }
        kit.Run("fw.main", Circuit.Water, 2, [new(52, 8.1f, 28.5f), new(52, 12.5f, 28.5f), new(52, 12.5f, 9.5f), new(43, 12.5f, 9.5f), new(-174, 12.5f, 9.5f)]);
        kit.Run("fw.branch", Circuit.Water, 2, [new(43, 8.1f, 28.5f), new(43, 12.5f, 28.5f), new(43, 12.5f, 9.5f)], end: PipeEnd.Knuckle);
        foreach (var x in new[] { 0f, -100f })
        {
            kit.Support(new(x - 1.2f, 8.3f, 2.4f, 2.4f, 0.2f), 10.5f);
        }
    }

    /// <summary>The step-up transformer beside the generator hall, with radiators, bushings and a conservator.</summary>
    private void Transformer()
    {
        kit.Slab(null, "stand.yard", new(134, -68, 22, 32, 1.5f), 0, PadHeight, bevel: 0.3f, edge: EdgeTone.Lo);
        var tank = new Plan(138, -62, 14, 22, 1);
        kit.Slab(null, "xfmr.tank", tank, PadHeight, 14, bevel: 0.4f);
        kit.With((p, _) => Details.FaceLines(tank, PadHeight + 2, PadHeight + 12, 2, p));
        foreach (var z in new[] { -60f, -51f })
        {
            kit.Slab(null, "xfmr.radiator", new(152, z, 2.5f, 7, 0.2f), PadHeight + 1, 11, steps: 2, bevel: 0);
        }
        foreach (var x in new[] { 141f, 145f, 149f })
        {
            kit.Post(null, "xfmr.bushing", x, -45, 1.1f, PadHeight + 14, 6, steps: 16);
            kit.Post(null, "xfmr.bushing.cap", x, -45, 1.7f, PadHeight + 20, 1, steps: 16);
        }
        foreach (var x in new[] { 139f, 150f })
        {
            kit.Slab(null, "xfmr.bracket", new(x, -59, 1, 2, 0.2f), PadHeight + 14, 4, steps: 2, bevel: 0);
        }
        var conservator = new Frame(new(0, PadHeight + 20, -58), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0));
        kit.Disc(null, "xfmr.conservator", conservator, 138, 152, 2);
        // A bus duct from the generator hall's pad edge.
        kit.Slab(null, "xfmr.busduct", new(131.5f, -48, 6.5f, 4, 0.3f), 10, 3, steps: 2, bevel: 0);
        kit.Slab(null, "xfmr.busduct.leg", new(132, -47, 2, 2, 0.2f), 0, 10, steps: 2, bevel: 0);
    }

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
        kit.Slab(null, name, plan, at.Y - w, w * 2, bevel: 0.4f);
        kit.Post(null, name + ".stem", at.X, at.Z, r * 0.35f, at.Y + w, r * 2.4f, steps: 12);
        var wheelY = at.Y + w + r * 2.4f;
        var wheel = r * 2;
        kit.Post(null, name + ".wheel", at.X, at.Z, wheel, wheelY, 0.5f, steps: 32);
        kit.With((p, _) => [
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
        kit.Slab(null, name + ".plinth", new(x - 3.5f, z0, 7, z1 - z0, 0.6f), 0, 2, steps: 2, bevel: 0.3f);
        const float y = 5.05f;
        var axis = new Frame(new(x, y, 0), new(0, 0, 1), new(1, 0, 0), new(0, 1, 0));
        kit.Disc(null, name + ".motor", axis, z0 + 1, z0 + 8, 2.6f);
        kit.With((p, _) => Enumerable.Range(0, 3).Select(i =>
        {
            var z = z0 + 2.5f + i * 2;
            return Details.Segment(EdgeTone.Lo, new(x, y + 2.6f, z), new(x, y + 2.6f, z + 1), p);
        }));
        kit.Disc(null, name + ".coupling", axis, z0 + 8, z0 + 9.4f, 1.3f);
        kit.Disc(null, name + ".pump", axis, z0 + 9.4f, z0 + 13, 3);
    }

    /// <summary>A control cabinet on a plinth: a door seam, a display and a handle on its front (+Z) face.</summary>
    private void Cabinet(string name, Plan plan, float height)
    {
        kit.Slab(null, name + ".plinth", plan with { X = plan.X - 0.5f, Z = plan.Z - 0.5f, W = plan.W + 1, D = plan.D + 1 }, 0, 1, steps: 2, bevel: 0.2f, edge: EdgeTone.Lo);
        kit.Slab(null, name, plan, 1, height, bevel: 0.3f);
        var front = plan.Z + plan.D;
        var mid = plan.X + plan.W / 2;
        kit.With((p, _) => !Details.Faces(0, 1, p) ? [] :
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
}
