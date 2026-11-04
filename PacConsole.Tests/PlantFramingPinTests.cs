using System.Globalization;
using System.Text;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>
/// The plant's framing and hit testing, value for value, against <c>Fixtures/framing.txt</c>: the projections the main
/// view and the thumbnails use, and the unit under each point of a grid at several cameras, one with a unit lifted.
/// Written before framing and hit testing left <see cref="PlantSolids"/> (for <see cref="PlantFraming"/> and
/// <see cref="UnitPicker"/>), so the move keeps every result. Set
/// PAC_PIN_WRITE=write to write the file again (only for an intended change).
/// </summary>
public class PlantFramingPinTests
{
    private const int Width = 1240, Height = 760, Step = 20;

    private static readonly (float Yaw, float Elevation, int Width, int Height, float Padding)[] Views =
    [
        (0.61f, 0.49f, 1240, 760, 8),
        (0f, 0.10f, 1240, 760, 8),
        (0f, 1.50f, 1240, 760, 8),
        (2.3f, 0.9f, 560, 360, 8),
        (4.4f, 0.3f, 390, 300, 24),
    ];

    private static readonly (string Name, UnitId? Unit, float Padding)[] Fits =
    [
        ("all", null, 6),
        ("tka", UnitId.Tka, 6),
        ("cnd", UnitId.Cnd, 24),
    ];

    private static readonly (float Yaw, float Elevation, UnitId? Lifted)[] HitCameras =
    [
        (0.61f, 0.49f, null),
        (2.3f, 0.9f, null),
        (4.4f, 0.3f, null),
        (0.61f, 0.49f, UnitId.Clg),
    ];

    [Test]
    public void Framing_and_hits_match_the_pinned_values()
    {
        var actual = Describe();
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "framing.txt");
        if (Environment.GetEnvironmentVariable("PAC_PIN_WRITE") == "write")
        {
            var source = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "Fixtures", "framing.txt");
            File.WriteAllText(source, actual);
            TestContext.Out.WriteLine($"wrote {source}");
            return;
        }
        File.Exists(path).Should().BeTrue("the pinned framing exists");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
        var lines = actual.Split('\n');
        lines.Should().HaveCount(expected.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            Same(lines[i], expected[i]).Should().BeTrue($"line {i + 1} reads \"{lines[i]}\", pinned \"{expected[i]}\"");
        }
    }

    /// <summary>Lines equal, numbers within 1e-3 of the pinned ones (float noise between runtimes).</summary>
    private static bool Same(string a, string b)
    {
        if (a == b) return true;
        var x = a.Split(' ');
        var y = b.Split(' ');
        if (x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++)
        {
            if (x[i] == y[i]) continue;
            if (!float.TryParse(x[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var p)
                || !float.TryParse(y[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var q)
                || MathF.Abs(p - q) > 1e-3f * MathF.Max(1, MathF.Abs(q)))
            {
                return false;
            }
        }
        return true;
    }

    private static string Describe()
    {
        var plant = PlantSolids.Default;
        var picker = new UnitPicker(plant);
        var text = new StringBuilder();
        foreach (var (yaw, elevation, w, h, padding) in Views)
        {
            var p = PlantFraming.ViewProjection(new CameraPose(yaw, elevation), w, h, padding);
            text.Append(Invariant($"view {yaw} {elevation} {w} {h} {padding} -> {p.Scale:0.000000} {p.CenterX:0.0000} {p.CenterY:0.0000}\n"));
        }
        foreach (var preset in CameraPresets.All)
        {
            foreach (var (name, unit, padding) in Fits)
            {
                IReadOnlyList<SolidPart> parts = unit is { } id ? plant.UnitWithPad(id) : plant.All;
                var p = PlantFraming.Fit(preset.Pose, parts, 300, 200, padding);
                text.Append(Invariant($"fit {preset.Key} {name} 300 200 {padding} -> {p.Scale:0.000000} {p.CenterX:0.0000} {p.CenterY:0.0000}\n"));
            }
        }
        foreach (var (yaw, elevation, lifted) in HitCameras)
        {
            var p = PlantFraming.ViewProjection(new CameraPose(yaw, elevation), Width, Height);
            var lift = lifted is { } l ? new Dictionary<UnitId, float> { [l] = 10 } : null;
            text.Append(Invariant($"hits {yaw} {elevation} {Width} {Height} lifted {lifted?.ToString() ?? "none"}\n"));
            for (var y = Step / 2; y < Height; y += Step)
            {
                for (var x = Step / 2; x < Width; x += Step)
                {
                    text.Append(picker.UnitAt(p, new(x, y), lift) is { } hit ? (char)('0' + (int)hit) : '.');
                }
                text.Append('\n');
            }
        }
        return text.ToString().TrimEnd('\n');
    }

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
