using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Tests;

public class GeometryTests
{
    private static readonly PlantGeometry Plant = PlantGeometry.Build();

    [Test]
    public void Every_unit_has_segments_an_anchor_and_a_radius()
    {
        foreach (var id in Enum.GetValues<UnitId>())
        {
            Plant.Units[id].Should().NotBeEmpty();
            Plant.Anchors.Should().ContainKey(id);
            Plant.Radii[id].Should().BePositive();
        }
    }

    [Test]
    public void Segment_count_matches_the_prototype_budget()
    {
        // The faithful port of the prototype's geo() draws 1,484 segments per frame
        // (46 grid + 1,438 solid), plus 18 for the condenser box added in console v3.
        // This guards against accidental blow-ups.
        var total = Plant.Grid.Length + Plant.AllSolid.Count();
        total.Should().Be(1502);
    }

    [TestCase(0.61f, 0.49f)]
    [TestCase(0f, 0.10f)]
    [TestCase(0f, 1.50f)]
    [TestCase(2.5f, 0.8f)]
    public void Whole_plant_stays_inside_the_reference_viewport(float yaw, float elevation)
    {
        var p = OrbitProjection.ForViewport(new CameraPose(yaw, elevation), OrbitProjection.ReferenceWidth, OrbitProjection.ReferenceHeight);
        foreach (var s in Plant.AllSolid)
        {
            foreach (var pt in new[] { p.Project(s.A), p.Project(s.B) })
            {
                pt.X.Should().BeInRange(0, OrbitProjection.ReferenceWidth);
                pt.Y.Should().BeInRange(0, OrbitProjection.ReferenceHeight);
            }
        }
    }

    [Test]
    public void Plan_view_collapses_height()
    {
        var p = new OrbitProjection(new CameraPose(0, MathF.PI / 2), 1, 0, 0);
        var ground = p.Project(new Vec3(10, 0, 5));
        var roof = p.Project(new Vec3(10, 50, 5));
        roof.Y.Should().BeApproximately(ground.Y, 1e-3f);
    }

    [Test]
    public void Wrap_takes_the_short_way_round()
    {
        CameraPose.Wrap(MathF.Tau + 0.1f).Should().BeApproximately(0.1f, 1e-5f);
        CameraPose.Wrap(-MathF.PI - 0.1f).Should().BeApproximately(MathF.PI - 0.1f, 1e-5f);
    }

    [Test]
    public void Sphere_fit_keeps_a_rotating_unit_inside_its_box()
    {
        for (var yaw = 0f; yaw < MathF.Tau; yaw += 0.2f)
        {
            var p = OrbitProjection.AroundSphere(new CameraPose(yaw, 0.45f), Plant.Centers[UnitId.Stk], Plant.Radii[UnitId.Stk], 135, 104, 6);
            foreach (var s in Plant.Units[UnitId.Stk])
            {
                var a = p.Project(s.A);
                a.X.Should().BeInRange(0, 135);
                a.Y.Should().BeInRange(0, 104);
            }
        }
    }
}
