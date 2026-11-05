using System.Collections.Concurrent;
using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>The camera framing of the plant: the main view's projection, which holds every yaw, and a tight fit for thumbnails.</summary>
public static class PlantFraming
{
    /// <summary>Headroom for a selected unit lifting off its pad (the renderer's full lift).</summary>
    private const float LiftAllowance = 10;

    private static readonly ConcurrentDictionary<int, (float MinX, float MaxX, float MinY, float MaxY)> _envelopes = new();

    /// <summary>
    /// The camera framing every whole-plant view shares, so the drawing and the tags placed over it
    /// use the same projection. It fits the plant's outline taken across every yaw at this elevation,
    /// so the frame is tight (the old bounding sphere left about 40% of the figure empty) and does not
    /// breathe while the camera orbits.
    /// </summary>
    public static OrbitProjection ViewProjection(CameraPose pose, float width, float height, float padding = 8)
    {
        var e = Envelope(pose.Elevation);
        var scale = MathF.Min((width - 2 * padding) / (e.MaxX - e.MinX), (height - 2 * padding) / (e.MaxY - e.MinY));
        return new OrbitProjection(pose, scale, width / 2 - scale * (e.MaxX + e.MinX) / 2, height / 2 - scale * (e.MaxY + e.MinY) / 2);
    }

    /// <summary>Fits the parts' hulls tightly inside a box, for fixed-camera thumbnails.</summary>
    public static OrbitProjection Fit(CameraPose pose, IEnumerable<SolidPart> parts, float width, float height, float padding)
    {
        var unit = new OrbitProjection(pose, 1, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var q in parts.SelectMany(p => p.Volume.Corners()))
        {
            var s = unit.Project(q);
            minX = MathF.Min(minX, s.X); maxX = MathF.Max(maxX, s.X);
            minY = MathF.Min(minY, s.Y); maxY = MathF.Max(maxY, s.Y);
        }
        var scale = MathF.Min((width - 2 * padding) / (maxX - minX), (height - 2 * padding) / (maxY - minY));
        return new OrbitProjection(pose, scale, width / 2 - scale * (minX + maxX) / 2, height / 2 - scale * (minY + maxY) / 2);
    }

    /// <summary>Screen extent of every part at unit scale over 36 yaws, plus a lifted unit's rise. Keyed to 0.005 rad.</summary>
    private static (float MinX, float MaxX, float MinY, float MaxY) Envelope(float elevation) =>
        _envelopes.GetOrAdd((int)MathF.Round(elevation * 200), key =>
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var corners = PlantSolids.Default.All.SelectMany(p => p.Volume.Corners()).ToArray();
            for (var i = 0; i < 36; i++)
            {
                var unit = new OrbitProjection(new CameraPose(i * MathF.PI / 18, key / 200f), 1, 0, 0);
                foreach (var q in corners)
                {
                    foreach (var y in new[] { q.Y, q.Y + LiftAllowance })
                    {
                        var s = unit.Project(q with { Y = y });
                        minX = MathF.Min(minX, s.X); maxX = MathF.Max(maxX, s.X);
                        minY = MathF.Min(minY, s.Y); maxY = MathF.Max(maxY, s.Y);
                    }
                }
            }
            return (minX, maxX, minY, maxY);
        });
}
