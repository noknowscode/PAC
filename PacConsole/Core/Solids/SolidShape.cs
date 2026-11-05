using PacConsole.Core.Geometry;

namespace PacConsole.Core.Solids;

/// <summary>A point on the floor plan. Y is up, so the plan is X and Z.</summary>
public readonly record struct PlanPoint(float X, float Z);

/// <summary>
/// A rounded rectangle on the floor plan: the Anatomy kit's plan <c>{x, y, w, d, r}</c>, with the
/// figure's y as the app's Z.
/// </summary>
public readonly record struct Plan(float X, float Z, float W, float D, float R)
{
    /// <summary>The outline, corner arcs of <paramref name="steps"/> segments each (the kit's <c>roundedPlan</c>).</summary>
    public PlanPoint[] Ring(int steps = 8)
    {
        var radius = MathF.Max(0, MathF.Min(R, MathF.Min(W / 2, D / 2)));
        var count = radius > 0 ? steps : 0;
        (float Cx, float Cz, float Start)[] corners =
        [
            (X + W - radius, Z + radius, -90),
            (X + W - radius, Z + D - radius, 0),
            (X + radius, Z + D - radius, 90),
            (X + radius, Z + radius, 180),
        ];
        var points = new List<PlanPoint>(4 * (count + 1));
        foreach (var (cx, cz, start) in corners)
        {
            for (var step = 0; step <= count; step++)
            {
                var angle = (start + (count > 0 ? 90f * step / count : 45)) * MathF.PI / 180;
                points.Add(new(cx + radius * MathF.Cos(angle), cz + radius * MathF.Sin(angle)));
            }
        }
        return [.. points];
    }
}

/// <summary>
/// One solid as it appears on screen, ready to paint back to front: the paper fill that hides what
/// is behind, the side faces bucketed into four shades, the top face, then lines.
/// Polygons are closed implicitly; polylines are open.
/// </summary>
public sealed class SolidShape
{
    public const int Shades = 4;

    /// <summary>Silhouette polygon, painted in the paper colour.</summary>
    public required Vec2[] Fill { get; init; }

    /// <summary>Side faces by shade, darkest (0) to lightest (3).</summary>
    public required List<Vec2[]>[] ShadeFaces { get; init; }

    /// <summary>Faces turned up toward the light (an extrusion's cap, a lathe's top band).</summary>
    public required List<Vec2[]> Top { get; init; }

    /// <summary>The visible silhouette and the edges where faces turn away.</summary>
    public required List<Vec2[]> Outline { get; init; }

    /// <summary>Edges between visible faces, drawn faintest.</summary>
    public required List<Vec2[]> Crease { get; init; }

    /// <summary>The inner line of a bevelled top edge.</summary>
    public required List<Vec2[]> Bevel { get; init; }
}
