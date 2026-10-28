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
        var placements = Place(pose, alerting: UnitId.Clg, Width, Height);

        foreach (var a in placements)
        {
            a.Top.Should().BeGreaterThanOrEqualTo(TagLayout.TopBand);
            a.Left.Should().BeGreaterThanOrEqualTo(0);
            (a.Left + a.Width).Should().BeLessThanOrEqualTo(Width);

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

    private static ImmutableArray<TagPlacement> Place(CameraPose pose, UnitId? alerting, float width, float height)
    {
        // The same projection and anchors PlantViewport places its tags with.
        var projection = PlantSolids.ViewProjection(pose, width, height);
        var requests = UnitCatalog.All.Select(u =>
        {
            var label = u.Id == alerting ? $"{u.Code} · alert" : u.Code;
            return new TagRequest(u.Id, projection.Project(PlantSolids.Default.Anchors[u.Id]), TagLayout.EstimateWidth(label));
        }).ToList();
        return TagLayout.Solve(requests, width);
    }
}
