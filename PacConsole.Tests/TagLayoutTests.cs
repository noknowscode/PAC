using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Telemetry;

namespace PacConsole.Tests;

/// <summary>
/// Ports the prototype's browser probe: orbit a full turn in 80 steps and assert no tag
/// overlaps another tag or reaches into the toolbar band. Before the layout pass the prototype
/// failed 28 of 80 positions.
/// </summary>
public class TagLayoutTests
{
    private static readonly PlantGeometry Plant = PlantGeometry.Build();

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
        var placements = Place(pose, alerting: UnitId.Cnd, width: OrbitProjection.ReferenceWidth, height: OrbitProjection.ReferenceHeight);

        foreach (var a in placements)
        {
            a.Top.Should().BeGreaterThanOrEqualTo(TagLayout.TopBand);
            a.Left.Should().BeGreaterThanOrEqualTo(0);
            (a.Left + a.Width).Should().BeLessThanOrEqualTo(OrbitProjection.ReferenceWidth);

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
        Place(CameraPose.Iso, alerting: null, 772, 540).Select(p => p.Unit).Should().Equal(Enum.GetValues<UnitId>());
    }

    private static ImmutableArray<TagPlacement> Place(CameraPose pose, UnitId? alerting, float width, float height)
    {
        var projection = OrbitProjection.ForViewport(pose, width, height);
        var requests = UnitCatalog.All.Select(u =>
        {
            var label = u.Id == alerting ? $"{u.Code} · alert" : u.Code;
            return new TagRequest(u.Id, projection.Project(Plant.Anchors[u.Id]), TagLayout.EstimateWidth(label));
        }).ToList();
        return TagLayout.Solve(requests, width);
    }
}
