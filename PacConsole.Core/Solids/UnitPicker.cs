using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Core.Solids;

/// <summary>
/// The unit under a screen point: each unit's hit area is the hull of its parts on screen (raised by its lift), and
/// where areas overlap the unit nearest the viewer wins.
/// </summary>
public sealed class UnitPicker(PlantSolids plant)
{
    private readonly Dictionary<UnitId, Vec3[]> _unitCorners = PlantSolids.Centres.Keys.ToDictionary(
        id => id, id => plant.All.Where(p => p.Unit == id).SelectMany(p => p.Volume.Corners()).ToArray());

    public UnitId? UnitAt(OrbitProjection p, Vec2 point, IReadOnlyDictionary<UnitId, float>? lift = null)
    {
        UnitId? best = null;
        var bestDepth = float.MinValue;
        foreach (var (id, corners) in _unitCorners)
        {
            var raise = lift is not null && lift.TryGetValue(id, out var l) ? l : 0;
            var hull = SolidBuilder.ConvexHull([.. corners.Select(q => p.Project(q with { Y = q.Y + raise }))]);
            if (!Contains(hull, point)) continue;
            var c = PlantSolids.Centres[id];
            var depth = p.Depth(new(c.X, 0, c.Z));
            if (depth > bestDepth) { bestDepth = depth; best = id; }
        }
        return best;
    }

    private static bool Contains(Vec2[] hull, Vec2 q)
    {
        var sign = 0;
        for (var i = 0; i < hull.Length; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Length];
            var cross = MathF.Sign((b.X - a.X) * (q.Y - a.Y) - (b.Y - a.Y) * (q.X - a.X));
            if (cross == 0) continue;
            if (sign != 0 && cross != sign) return false;
            sign = cross;
        }
        return hull.Length >= 3;
    }
}
