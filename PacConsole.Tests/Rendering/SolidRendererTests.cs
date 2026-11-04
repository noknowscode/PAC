using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Rendering;
using SkiaSharp;

namespace PacConsole.Tests.Rendering;

/// <summary>
/// The plant renderer offscreen, against frames drawn before it was split into its parts (Fixtures/render). Each case
/// walks one of its paths: a camera move draws directly; the same frame again records the still runs and replays them;
/// once the workers have rasterized them, the runs draw as images; a lift at a still camera draws part images; an
/// isolated system adds its live motion. Set PAC_RENDER_GOLDEN=write to draw the references again.
/// </summary>
public class SolidRendererTests
{
    private const int Width = 560, Height = 360;
    private static readonly ImmutableArray<SolidPart> Parts = PlantSolids.Default.All;

    private static SKColor C(uint argb) => new(argb);

    /// <summary>The Light palette's values, fixed here so the references never depend on the app's resources.</summary>
    internal static readonly SolidPalette Light = new(
        Paper: C(0xFFF4F5F6),
        Shades: [C(0xFFC9D4DE), C(0xFFD6DFE7), C(0xFFE2E9EF), C(0xFFECF1F5)],
        Top: C(0xFFFFFFFF),
        LitFace: C(0xFFE3EEF3),
        LitShade: C(0xFFCFE0E8),
        LitTop: C(0xFFF2F7F9),
        Hi: C(0xFF1E2833),
        Mid: C(0xFF5E6E80),
        Lo: C(0xFF9AA9B8),
        Faint: C(0xFFC3CED8),
        Lit: C(0xFF2F5D73),
        Alert: C(0xFFC2410C),
        Leader: C(0xFF8A939E),
        Shadow: C(0x1A1E2833),
        Silhouette: C(0xFF2C3D50),
        Hairline: 0.75f,
        SilhouetteStroke: 1.25f,
        LitStroke: 1.75f)
    {
        Circuits = new Dictionary<Circuit, (SKColor, SKColor)>
        {
            [Circuit.Steam] = (C(0xFFE8C9D0), C(0xFFD4A3AF)),
            [Circuit.Water] = (C(0xFFCBDFEC), C(0xFF9EC1D8)),
            [Circuit.Cooling] = (C(0xFFC7E3DF), C(0xFF96C9C3)),
            [Circuit.Fuel] = (C(0xFFDCD3E8), C(0xFFB9A8D0)),
        },
        Systems = new Dictionary<PlantSystem, SKColor>
        {
            [PlantSystem.SteamWater] = C(0xFFB84D66),
            [PlantSystem.Cooling] = C(0xFF1F8A7E),
            [PlantSystem.FuelFlue] = C(0xFF7A5BA8),
            [PlantSystem.Electrical] = C(0xFFA87A12),
            [PlantSystem.Alarms] = C(0xFFC2410C),
        },
    };

    private static readonly ImmutableArray<UnitId> Alerting = [UnitId.Clg];

    private static ImmutableDictionary<UnitId, float> Lifts(UnitId? unit = null, float lift = 0) =>
        UnitCatalog.All.ToImmutableDictionary(u => u.Id, u => u.Id == unit ? lift : 0f);

    [Test]
    public async Task A_still_plant_draws_the_same_directly_recorded_and_from_its_cached_layers()
    {
        using var renderer = new SolidRenderer { Palette = Light };
        var ready = new SemaphoreSlim(0);
        renderer.StillLayersReady += () => ready.Release();
        var frame = new SolidFrame(UnitId.Trb, Alerting, Lifts(), Seconds: 2.5);
        var projection = PlantSolids.ViewProjection(CameraPose.Iso, Width, Height);

        var direct = Draw(renderer, projection, frame);
        var recorded = Draw(renderer, projection, frame);
        // Two more still frames start the workers.
        Draw(renderer, projection, frame).Dispose();
        Draw(renderer, projection, frame).Dispose();
        (await ready.WaitAsync(TimeSpan.FromSeconds(20))).Should().BeTrue("the workers rasterize the still runs");
        await ready.WaitAsync(TimeSpan.FromSeconds(2));
        var layered = Draw(renderer, projection, frame);

        Match("still-direct", direct);
        Match("still-recorded", recorded);
        Match("still-layered", layered);
    }

    [Test]
    public void A_lift_at_a_still_camera_draws_the_lifted_unit()
    {
        using var renderer = new SolidRenderer { Palette = Light };
        var projection = PlantSolids.ViewProjection(CameraPose.Iso, Width, Height);
        Draw(renderer, projection, new SolidFrame(UnitId.Gen, Alerting, Lifts(), Seconds: 1));

        var lifted = Draw(renderer, projection, new SolidFrame(UnitId.Gen, Alerting, Lifts(UnitId.Gen, CameraRig.FullLift * 0.6f), Seconds: 1, Hovered: UnitId.Trb));

        Match("lift", lifted);
    }

    [TestCase(PlantSystem.Electrical)]
    [TestCase(PlantSystem.FuelFlue)]
    [TestCase(PlantSystem.Cooling)]
    [TestCase(PlantSystem.SteamWater)]
    [TestCase(PlantSystem.Alarms)]
    public void An_isolated_system_draws_in_its_ink_with_its_motion(PlantSystem system)
    {
        using var renderer = new SolidRenderer { Palette = Light };
        var pose = new CameraPose(0.9f, 0.55f);
        var projection = PlantSolids.ViewProjection(pose, Width, Height);

        var bitmap = Draw(renderer, projection, new SolidFrame(UnitId.Clg, Alerting, Lifts(UnitId.Clg, CameraRig.FullLift), Seconds: 1.3, System: system));

        Match($"system-{system}", bitmap);
    }

    [Test]
    public void The_plan_view_without_details_or_motion()
    {
        using var renderer = new SolidRenderer { Palette = Light };
        var preset = CameraPresets.All.Single(p => p.Key == "plan");
        var projection = PlantSolids.ViewProjection(preset.Pose, Width, Height);

        var bitmap = Draw(renderer, projection, new SolidFrame(null, [], null, Details: false, Motion: false));

        Match("plan-plain", bitmap);
    }

    private static SKBitmap Draw(SolidRenderer renderer, OrbitProjection projection, SolidFrame frame)
    {
        var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(Light.Paper);
        renderer.Draw(canvas, Parts, projection, frame);
        canvas.Flush();
        return bitmap;
    }

    /// <summary>
    /// Compares with the reference, allowing anti-aliasing noise between Skia builds and CPUs: a channel may differ by a
    /// few levels, and at most 0.2 % of pixels by more.
    /// </summary>
    private static void Match(string name, SKBitmap bitmap)
    {
        using (bitmap)
        {
            var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "render", name + ".png");
            if (Environment.GetEnvironmentVariable("PAC_RENDER_GOLDEN") == "write")
            {
                var source = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures", "render", name + ".png");
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(source, data.ToArray());
                TestContext.Out.WriteLine($"wrote {source}");
                return;
            }
            File.Exists(path).Should().BeTrue($"the reference {name}.png exists");
            using var expected = SKBitmap.Decode(path);
            expected.Width.Should().Be(bitmap.Width);
            expected.Height.Should().Be(bitmap.Height);
            int off = 0, worst = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    SKColor a = bitmap.GetPixel(x, y), b = expected.GetPixel(x, y);
                    var d = Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                    worst = Math.Max(worst, d);
                    if (d > 6) off++;
                }
            }
            off.Should().BeLessThanOrEqualTo(bitmap.Width * bitmap.Height / 500, $"{name}: {off} pixels differ by more than 6 levels (worst {worst})");
        }
    }
}
