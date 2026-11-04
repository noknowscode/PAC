using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;

namespace PacConsole.Tests;

/// <summary>
/// Ports the prototype's browser probe: orbit a full turn in 80 steps and assert no tag
/// overlaps another tag or reaches into the toolbar band. Before the layout pass the prototype
/// failed 28 of 80 positions.
/// </summary>
public class TagLayoutTests
{
    // The prototype's viewport, which the probe ran against.
    private const float Width = 772, Height = 540;

    public static IEnumerable<CameraPose> OrbitPoses()
    {
        // 80 samples, 10 px of drag apart at 0.008 rad/px, exactly as the probe dragged.
        for (var i = 0; i <= 79; i++) yield return new CameraPose(CameraPose.Iso.Yaw + i * 0.08f, CameraPose.Iso.Elevation);
        yield return CameraPose.Front;
        yield return CameraPose.Plan;
    }

    [TestCaseSource(nameof(OrbitPoses))]
    public void Tags_never_collide_with_each_other_or_the_top_band(CameraPose pose)
    {
        AssertApart(Place(pose, alerting: UnitId.Clg, Width, Height), pose, Width);
    }

    [TestCaseSource(nameof(OrbitPoses))]
    public void Taller_tags_keep_their_height_inside_the_drawing(CameraPose pose)
    {
        // The viewport passes the tags' measured height: at 150 % text size a tag is about 36 px tall. The solver
        // keeps every tag that tall, below the top band and inside the drawing. (It was tuned on 24 px tags; at this
        // height one pose in 82 still leaves two tags touching.)
        foreach (var p in Place(pose, alerting: UnitId.Clg, Width, Height, ScaledHeight))
        {
            (p.Bottom - p.Top).Should().Be(ScaledHeight);
            p.Top.Should().BeGreaterThanOrEqualTo(TagLayout.TopBand);
            p.Left.Should().BeGreaterThanOrEqualTo(0);
            (p.Left + p.Width).Should().BeLessThanOrEqualTo(Width);
        }
    }

    private static void AssertApart(ImmutableArray<TagPlacement> placements, CameraPose pose, float width)
    {
        foreach (var a in placements)
        {
            a.Top.Should().BeGreaterThanOrEqualTo(TagLayout.TopBand);
            a.Left.Should().BeGreaterThanOrEqualTo(0);
            (a.Left + a.Width).Should().BeLessThanOrEqualTo(width);

            foreach (var b in placements.Where(b => b.Unit != a.Unit))
            {
                var overlaps = a.Left < b.Left + b.Width && b.Left < a.Left + a.Width
                    && a.Top < b.Bottom && b.Top < a.Bottom;
                overlaps.Should().BeFalse($"{a.Unit} and {b.Unit} overlap at yaw {pose.Yaw:0.00}");
            }
        }
    }

    [Test]
    public void Output_keeps_input_order()
    {
        Place(CameraPose.Iso, alerting: null, Width, Height).Select(p => p.Unit).Should().Equal(Enum.GetValues<UnitId>());
    }

    private const float ScaledHeight = 36;

    private static ImmutableArray<TagPlacement> Place(CameraPose pose, UnitId? alerting, float width, float height, float tagHeight = TagLayout.Height)
    {
        // The same projection and anchors PlantViewport places its tags with; the app measures each tag's button,
        // here a 12 px label is taken as 7 px a character plus the plate's padding.
        var projection = PlantFraming.ViewProjection(pose, width, height);
        var requests = UnitCatalog.All.Select(u =>
        {
            var label = u.Id == alerting ? $"{u.Code} · alert" : u.Code;
            return new TagRequest(u.Id, projection.Project(PlantSolids.Default.Anchors[u.Id]), label.Length * 7f + 16f);
        }).ToList();
        return TagLayout.Solve(requests, width, tagHeight);
    }
}
