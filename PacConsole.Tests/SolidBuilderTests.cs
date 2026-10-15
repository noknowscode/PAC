using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>
/// The C# solids against the Anatomy kit itself: <c>Fixtures/tka-reference.json</c> is what the kit
/// draws for TKA-02 at the figure's hero camera (docs/anatomy/plant/export-tka.mjs).
/// </summary>
public partial class SolidBuilderTests
{
    // The kit writes coordinates rounded to tenths.
    private const float Tolerance = 0.06f;

    private static readonly JsonElement Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "tka-reference.json"))).RootElement;

    /// <summary>The figure's camera as an app projection: yaw = 90° - azimuth, scale × √1.6.</summary>
    private static OrbitProjection HeroProjection()
    {
        var p = Fixture.GetProperty("projection");
        var origin = p.GetProperty("origin");
        var azimuth = p.GetProperty("azimuth").GetSingle();
        var elevation = p.GetProperty("elevation").GetSingle();
        var pose = new CameraPose((90 - azimuth) * MathF.PI / 180, elevation * MathF.PI / 180);
        return new OrbitProjection(pose, p.GetProperty("scale").GetSingle() * MathF.Sqrt(1.6f), origin[0].GetSingle(), origin[1].GetSingle());
    }

    private static IEnumerable<(string Name, JsonElement Part)> KitParts() =>
        Fixture.GetProperty("parts").EnumerateArray().Select(e => (e.GetProperty("name").GetString()!, e));

    private static SolidShape Build(JsonElement args, OrbitProjection p)
    {
        float F(string name) => args.GetProperty(name).GetSingle();
        Vec3 V(string name)
        {
            var a = args.GetProperty(name);
            return new(a[0].GetSingle(), a[2].GetSingle(), a[1].GetSingle()); // figure (x, y, z) → app (x, z, y)
        }
        switch (args.GetProperty("kind").GetString())
        {
            case "slab":
                var plan = args.GetProperty("plan");
                float P(string name) => plan.GetProperty(name).GetSingle();
                return SolidBuilder.Slab(new(P("x"), P("y"), P("w"), P("d"), P("r")), F("z"), F("h"), p, args.GetProperty("steps").GetInt32(), F("bevel"));
            case "cylinder":
                return SolidBuilder.Cylinder(F("x"), F("y"), F("r"), F("z"), F("h"), p, args.GetProperty("steps").GetInt32(), F("bevel"));
            default:
                var frame = new Frame(V("o"), V("a"), V("u"), V("v"));
                var profile = args.GetProperty("profile").EnumerateArray().Select(k => (k[0].GetSingle(), k[1].GetSingle())).ToArray();
                return args.TryGetProperty("raw", out _)
                    ? SolidBuilder.Disc(profile[0].Item1, profile[^1].Item1, profile[1].Item2, frame, p)
                    : SolidBuilder.Turned(profile, frame, p);
        }
    }

    [Test]
    public void Extrusions_match_the_kit_point_for_point()
    {
        var p = HeroProjection();
        var checkedParts = 0;
        foreach (var (name, part) in KitParts())
        {
            var args = part.GetProperty("args");
            if (args.GetProperty("kind").GetString() == "lathe") continue;
            var shape = Build(args, p);

            // The hull may keep or drop a point that sits on a straight edge, so compare what it covers.
            var kitFill = Parse(part.GetProperty("fill").GetString()!);
            Area([shape.Fill]).Should().BeApproximately(Area(kitFill), 0.2f + 0.002f * Area(kitFill), name + " fill");
            kitFill[0].Should().OnlyContain(q => shape.Fill.Any(m => MathF.Abs(m.X - q.X) <= Tolerance && MathF.Abs(m.Y - q.Y) <= Tolerance), name + " fill");
            Same(name + " outline", shape.Outline, part.GetProperty("outline").GetString()!);
            Same(name + " crease", shape.Crease, part.GetProperty("crease").GetString()!);
            Same(name + " top", shape.Top, part.GetProperty("top").GetString()!);
            Same(name + " bevel", shape.Bevel, part.GetProperty("bevel").GetString()!);
            SameShades(name, shape, part.GetProperty("shades"));
            checkedParts++;
        }
        checkedParts.Should().Be(11);
    }

    [Test]
    public void Turned_parts_match_the_kit_by_shaded_area()
    {
        var p = HeroProjection();
        foreach (var (name, part) in KitParts())
        {
            var args = part.GetProperty("args");
            if (args.GetProperty("kind").GetString() != "lathe") continue;
            var shape = Build(args, p);

            var shades = part.GetProperty("shades");
            for (var s = 0; s < SolidShape.Shades; s++)
            {
                Area(shape.ShadeFaces[s]).Should().BeApproximately(Area(Parse(shades[s].GetString()!)), 0.5f + 0.01f * Area(shape.ShadeFaces[s]), $"{name} shade {s}");
            }
            Area(shape.Top).Should().BeApproximately(Area(Parse(part.GetProperty("top").GetString()!)), 0.5f + 0.01f * Area(shape.Top), $"{name} top");
            Area([shape.Fill]).Should().BeApproximately(Area(Parse(part.GetProperty("fill").GetString()!)), 0.01f * Area([shape.Fill]), $"{name} fill");
            Same(name + " crease", shape.Crease, part.GetProperty("crease").GetString()!);
        }
    }

    [Test]
    public void Spike_unit_has_every_part_of_the_figure()
    {
        PlantSolids.Tka().Should().HaveCount(KitParts().Count() + 6, "the fixture leaves out the three steps, the skid, the coupling and the pump");
    }

    [TestCase(0)]
    [TestCase(45)]
    [TestCase(90)]
    [TestCase(135)]
    [TestCase(180)]
    [TestCase(225)]
    [TestCase(270)]
    [TestCase(315)]
    public void Roofs_vents_and_caps_paint_after_what_they_stand_on(int yawDegrees)
    {
        var p = new OrbitProjection(new CameraPose(yawDegrees * MathF.PI / 180, 0.5f), 2, 400, 300);
        var parts = PlantSolids.Tka();
        var order = DepthOrder.Sort(parts.Select(x => x.Volume).ToList(), p);
        var rank = new int[order.Length];
        for (var k = 0; k < order.Length; k++) rank[order[k]] = k;

        int[] Named(string name) => [.. Enumerable.Range(0, parts.Length).Where(i => parts[i].Name == name)];
        var pad = Named("stand.pad").Single();
        foreach (var i in Enumerable.Range(0, parts.Length).Where(i => i != pad)) rank[i].Should().BeGreaterThan(rank[pad]);
        var tanks = Named("tka.tank");
        var roofs = Named("tka.roof");
        var vents = Named("tka.vent");
        var caps = Named("tka.cap");
        for (var k = 0; k < 2; k++)
        {
            rank[roofs[k]].Should().BeGreaterThan(rank[tanks[k]]);
            rank[vents[k]].Should().BeGreaterThan(rank[roofs[k]]);
            rank[caps[k]].Should().BeGreaterThan(rank[vents[k]]);
        }
    }

    [Test]
    public void A_separating_plane_puts_the_near_part_last()
    {
        // Two boxes side by side on X. Viewer at yaw 90° looks from +X, so the +X box is nearer.
        var west = Volume.Box(new(-10, -5, 8, 10, 0), 0, 10);
        var east = Volume.Box(new(2, -5, 8, 10, 0), 0, 10);
        var fromEast = new OrbitProjection(new CameraPose(MathF.PI / 2, 0.5f), 1, 0, 0);
        var fromWest = new OrbitProjection(new CameraPose(-MathF.PI / 2, 0.5f), 1, 0, 0);

        DepthOrder.Compare(west, east, fromEast, 0, 0).Should().Be(-1);
        DepthOrder.Compare(west, east, fromWest, 0, 0).Should().Be(1);
    }

    /// <summary>
    /// Every kit face is one of mine, in the same shade band or the next: a face lit exactly on a
    /// band boundary (0.25, 0.5, 0.75) can round either way in float.
    /// </summary>
    private static void SameShades(string name, SolidShape shape, JsonElement shades)
    {
        var mine = shape.ShadeFaces.SelectMany((faces, band) => faces.Select(f => (Band: band, Face: f))).ToList();
        var theirs = Enumerable.Range(0, SolidShape.Shades).SelectMany(band => Parse(shades[band].GetString()!).Select(f => (Band: band, Face: f))).ToList();
        mine.Should().HaveCount(theirs.Count, $"{name} shaded faces");
        foreach (var (band, face) in theirs)
        {
            var match = mine.Where(m => m.Face.Length == face.Length && m.Face.Zip(face).All(q => MathF.Abs(q.First.X - q.Second.X) <= Tolerance && MathF.Abs(q.First.Y - q.Second.Y) <= Tolerance)).ToList();
            match.Should().ContainSingle($"{name} face at {face[0]}");
            Math.Abs(match[0].Band - band).Should().BeLessThanOrEqualTo(1, $"{name} face at {face[0]}");
        }
    }

    private static void Same(string what, IReadOnlyList<Vec2[]> mine, string kit)
    {
        var theirs = Parse(kit);
        mine.Should().HaveCount(theirs.Count, what);
        for (var i = 0; i < theirs.Count; i++)
        {
            mine[i].Should().HaveCount(theirs[i].Length, $"{what} [{i}]");
            for (var j = 0; j < theirs[i].Length; j++)
            {
                mine[i][j].X.Should().BeApproximately(theirs[i][j].X, Tolerance, $"{what} [{i}][{j}].X");
                mine[i][j].Y.Should().BeApproximately(theirs[i][j].Y, Tolerance, $"{what} [{i}][{j}].Y");
            }
        }
    }

    /// <summary>An SVG path of M/L/Z commands, one array per subpath.</summary>
    private static List<Vec2[]> Parse(string d)
    {
        var paths = new List<Vec2[]>();
        List<Vec2>? current = null;
        foreach (Match m in Command().Matches(d))
        {
            if (m.Groups[1].Value == "Z") continue;
            if (m.Groups[1].Value == "M")
            {
                if (current is not null) paths.Add([.. current]);
                current = [];
            }
            current!.Add(new(float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)));
        }
        if (current is not null) paths.Add([.. current]);
        return paths;
    }

    private static float Area(IEnumerable<Vec2[]> polygons) => polygons.Sum(points =>
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(area / 2);
    });

    [GeneratedRegex(@"([MLZ])\s*(?:(-?[\d.]+)\s+(-?[\d.]+))?")]
    private static partial Regex Command();
}
