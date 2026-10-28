using PacConsole.Core.Geometry;

namespace PacConsole.Tests;

public class GeometryTests
{
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
}
