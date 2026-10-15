using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

public class PlantSolidsTests
{
    private static readonly PlantSolids Plant = PlantSolids.Default;

    [Test]
    public void Every_unit_has_lit_parts_and_a_pad()
    {
        foreach (var id in Enum.GetValues<UnitId>())
        {
            Plant.All.Where(p => p.Unit == id).Should().Contain(p => p.Lit, id.ToString());
            Plant.Pads.Should().ContainKey(id);
            Plant.UnitWithPad(id)[0].Should().BeSameAs(Plant.Pads[id]);
        }
    }

    [Test]
    public void Every_anchor_sits_on_top_of_its_own_unit()
    {
        foreach (var (id, anchor) in Plant.Anchors)
        {
            var c = PlantSolids.Centres[id];
            MathF.Abs(anchor.X - c.X).Should().BeLessThan(PlantSolids.PadHalf, id.ToString());
            MathF.Abs(anchor.Z - c.Z).Should().BeLessThan(PlantSolids.PadHalf, id.ToString());
            anchor.Y.Should().Be(Plant.All.Where(p => p.Unit == id).Max(p => p.Volume.Y1), id.ToString());
        }
    }

    [TestCase(0.61f, 0.49f)]
    [TestCase(0f, 0.10f)]
    [TestCase(0f, 1.50f)]
    [TestCase(4.8f, 1.24f)]
    public void The_whole_plant_fits_its_frame(float yaw, float elevation)
    {
        const float w = 1240, h = 760;
        var p = PlantSolids.ViewProjection(new CameraPose(yaw, elevation), w, h);
        foreach (var part in Plant.All)
        {
            var v = part.Volume;
            foreach (var q in v.Footprint)
            {
                foreach (var y in new[] { v.Y0, v.Y1 })
                {
                    var s = p.Project(new(q.X, y, q.Z));
                    s.X.Should().BeInRange(0, w, part.Name);
                    s.Y.Should().BeInRange(0, h, part.Name);
                }
            }
        }
    }

    [TestCase(0.61f, 0.49f)]
    [TestCase(2.3f, 0.9f)]
    [TestCase(4.4f, 0.3f)]
    public void Clicking_a_units_top_selects_that_unit(float yaw, float elevation)
    {
        var p = PlantSolids.ViewProjection(new CameraPose(yaw, elevation), 1240, 760);
        foreach (var (id, anchor) in Plant.Anchors)
        {
            // Just below the anchor: on the unit's tallest part, where a person would click.
            var hit = p.Project(anchor with { Y = anchor.Y - 1 });
            Plant.UnitAt(p, hit).Should().Be(id);
        }
    }

    [Test]
    public void Clicking_empty_space_selects_nothing()
    {
        var p = PlantSolids.ViewProjection(CameraPose.Iso, 1240, 760);
        Plant.UnitAt(p, new(5, 5)).Should().BeNull();
    }

    [Test]
    public void A_lifted_unit_is_hit_where_it_is_drawn()
    {
        var p = PlantSolids.ViewProjection(CameraPose.Iso, 1240, 760);
        var anchor = Plant.Anchors[UnitId.Sil];
        var lifted = p.Project(anchor with { Y = anchor.Y + 10 - 1 });
        Plant.UnitAt(p, lifted, new Dictionary<UnitId, float> { [UnitId.Sil] = 10 }).Should().Be(UnitId.Sil);
    }

    [Test]
    public void Part_count_matches_the_figure()
    {
        // Stand 8 feet + plate + strip + label + 6 pads = 17; STK 12; TKA 19; GEN 16; TRB 27;
        // CND 8; SIL 14; pipework 6 piers + 8 sleepers + 4 tubes + 8 flanges = 26.
        Plant.All.Should().HaveCount(139);
        Plant.All.Count(p => p.Name is "rack.north" or "rack.south" or "steam" or "fuel").Should().Be(4);
    }

    [Test]
    public void Every_unit_part_stands_inside_its_pad()
    {
        foreach (var part in Plant.All.Where(p => p.Unit is not null))
        {
            var c = PlantSolids.Centres[part.Unit!.Value];
            part.Volume.Footprint.Should().OnlyContain(
                q => MathF.Abs(q.X - c.X) <= PlantSolids.PadHalf + 0.01f && MathF.Abs(q.Z - c.Z) <= PlantSolids.PadHalf + 0.01f,
                $"{part.Name} must lift with its unit without leaving the pad");
        }
    }

    /// <summary>
    /// The two rack headers run 6 apart with 3.41-radius flanges, so their flanges intersect in
    /// the figure itself. Two identical discs side by side order correctly by their centres.
    /// </summary>
    private static bool KnownOverlap(SolidPart a, SolidPart b) =>
        a.Name is "rack.north.flange" or "rack.south.flange" && b.Name is "rack.north.flange" or "rack.south.flange";

    [Test]
    public void No_two_hulls_interpenetrate()
    {
        // With every pair separable, the separating-plane order is exact at any camera.
        var all = Plant.All;
        var bad = new List<string>();
        for (var i = 0; i < all.Length; i++)
        {
            for (var j = i + 1; j < all.Length; j++)
            {
                if (!KnownOverlap(all[i], all[j]) && !DepthOrder.Separable(all[i].Volume, all[j].Volume)) bad.Add($"{all[i].Name} × {all[j].Name}");
            }
        }
        bad.Should().BeEmpty();
    }

    [Test]
    public void No_two_hulls_interpenetrate_with_a_unit_lifted()
    {
        foreach (var lifted in Enum.GetValues<UnitId>())
        {
            var volumes = Plant.All.Select(p => p.Unit == lifted ? p.Volume.Raised(10) : p.Volume).ToArray();
            for (var i = 0; i < volumes.Length; i++)
            {
                for (var j = i + 1; j < volumes.Length; j++)
                {
                    if (KnownOverlap(Plant.All[i], Plant.All[j])) continue;
                    DepthOrder.Separable(volumes[i], volumes[j]).Should().BeTrue($"{Plant.All[i].Name} × {Plant.All[j].Name} with {lifted} lifted");
                }
            }
        }
    }

    /// <summary>
    /// The audit's depthOrder check, ported: cast a view ray through every pixel, find the nearest
    /// hull, and fail if the painter's order lets a farther hull paint over it.
    /// </summary>
    [Test]
    public void Painter_order_agrees_with_a_ray_cast_z_buffer([Values(0.08f, 0.6f, 1.52f)] float elevation) =>
        CheckZBuffer(elevation, lifted: null);

    [Test]
    public void Painter_order_holds_with_the_alerting_unit_lifted() => CheckZBuffer(0.6f, lifted: UnitId.Cnd);

    private static void CheckZBuffer(float elevation, UnitId? lifted)
    {
        var volumes = Plant.All.Select(p => lifted is not null && p.Unit == lifted ? p.Volume.Raised(10) : p.Volume).ToArray();
        var failures = new List<string>();
        for (var k = 0; k < 24; k++)
        {
            var pose = new CameraPose(k * MathF.Tau / 24, elevation);
            var p = new OrbitProjection(pose, 1, 0, 0);
            var order = DepthOrder.Sort(volumes, p);
            var rank = new int[order.Length];
            for (var i = 0; i < order.Length; i++) rank[order[i]] = i;
            failures.AddRange(ZBuffer(volumes, p, rank).Select(f => $"yaw {k * 15}°: {Plant.All[f.Far].Name} over {Plant.All[f.Near].Name}"));
        }
        failures.Distinct().Should().BeEmpty();
    }

    private static IEnumerable<(int Far, int Near)> ZBuffer(Volume[] volumes, OrbitProjection p, int[] rank)
    {
        float sy = p.SinYaw, cy = p.CosYaw, se = p.SinElevation, ce = p.CosElevation;
        var e1 = new Vec3(cy, 0, -sy);             // one screen unit right
        var e2 = new Vec3(sy * se, -ce, cy * se);  // one screen unit down
        var d = new Vec3(sy * ce, se, cy * ce);    // toward the viewer, screen-still

        var boxes = volumes.Select(v =>
        {
            var pts = v.Bottom.Select(q => p.Project(new(q.X, v.Y0, q.Z))).Concat(v.Top.Select(q => p.Project(new(q.X, v.Y1, q.Z)))).ToArray();
            return (L: pts.Min(q => q.X), R: pts.Max(q => q.X), T: pts.Min(q => q.Y), B: pts.Max(q => q.Y));
        }).ToArray();
        var halves = volumes.Select(HalfPlanes).ToArray();

        var all = (L: boxes.Min(b => b.L), R: boxes.Max(b => b.R), T: boxes.Min(b => b.T), B: boxes.Max(b => b.B));
        const float step = 0.75f;
        var bad = new HashSet<(int, int)>();
        for (var x = all.L + step / 2; x < all.R; x += step)
        {
            for (var y = all.T + step / 2; y < all.B; y += step)
            {
                var origin = new Vec3(e1.X * x + e2.X * y, e1.Y * x + e2.Y * y, e1.Z * x + e2.Z * y);
                int nearest = -1, top = -1;
                float nearestT = float.MinValue;
                for (var i = 0; i < volumes.Length; i++)
                {
                    var b = boxes[i];
                    if (x < b.L || x > b.R || y < b.T || y > b.B) continue;
                    if (Exit(volumes[i], halves[i], origin, d) is not { } t) continue;
                    if (t > nearestT) { nearestT = t; nearest = i; }
                    if (top < 0 || rank[i] > rank[top]) top = i;
                }
                if (top < 0 || top == nearest) continue;
                // A tie on a shared face is fine; anything farther than that is a misorder.
                var topT = Exit(volumes[top], halves[top], origin, d)!.Value;
                if (nearestT - topT > 0.05f) bad.Add((top, nearest));
            }
        }
        return bad;
    }

    /// <summary>The ray parameter where it leaves the hull toward the viewer (its visible surface), or null if it misses.</summary>
    private static float? Exit(Volume v, (Vec3 N, float C)[] halves, Vec3 o, Vec3 d)
    {
        float t0 = float.MinValue, t1 = float.MaxValue;
        foreach (var (n, c) in halves)
        {
            // Inside when n·P <= c.
            var dn = n.X * d.X + n.Y * d.Y + n.Z * d.Z;
            var on = n.X * o.X + n.Y * o.Y + n.Z * o.Z;
            if (MathF.Abs(dn) < 1e-9f)
            {
                if (on > c) return null;
                continue;
            }
            var t = (c - on) / dn;
            if (dn > 0) t1 = MathF.Min(t1, t); else t0 = MathF.Max(t0, t);
        }
        return t0 < t1 - 1e-4f ? t1 : null;
    }

    /// <summary>The hull as half-spaces: top, bottom, and one (possibly slanted) face per footprint edge.</summary>
    private static (Vec3 N, float C)[] HalfPlanes(Volume v)
    {
        var halves = new List<(Vec3, float)> { (new(0, 1, 0), v.Y1), (new(0, -1, 0), -v.Y0) };
        var bottom = v.Bottom;
        var top = v.Top;
        float cx = bottom.Average(q => q.X), cz = bottom.Average(q => q.Z), cy = (v.Y0 + v.Y1) / 2;
        for (var i = 0; i < bottom.Length; i++)
        {
            var a = new Vec3(bottom[i].X, v.Y0, bottom[i].Z);
            var b = new Vec3(bottom[(i + 1) % bottom.Length].X, v.Y0, bottom[(i + 1) % bottom.Length].Z);
            var t = new Vec3(top[i].X, v.Y1, top[i].Z);
            var e = b - a;
            var f = t - a;
            var n = new Vec3(e.Y * f.Z - e.Z * f.Y, e.Z * f.X - e.X * f.Z, e.X * f.Y - e.Y * f.X);
            if (n.X * (cx - a.X) + n.Y * (cy - a.Y) + n.Z * (cz - a.Z) > 0) n = new(-n.X, -n.Y, -n.Z); // point outward
            halves.Add((n, n.X * a.X + n.Y * a.Y + n.Z * a.Z));
        }
        return [.. halves];
    }
}
