using PacConsole.Core.Plant;

namespace PacConsole.Core.Geometry;

public readonly record struct TagRequest(UnitId Unit, Vec2 Anchor, float Width);

/// <summary>Where a tag lands: its horizontal centre and its bottom edge, plus the anchor its leader line starts from.</summary>
public readonly record struct TagPlacement(UnitId Unit, Vec2 Anchor, float CenterX, float Bottom, float Width, float Height)
{
    public float Left => CenterX - Width / 2;
    public float Top => Bottom - Height;
}

/// <summary>
/// Places the floating unit tags so they never overlap each other or the toolbar band.
/// Lowest tag first; a tag that would overlap an already-placed one stacks above it, and if
/// that would push it into the top band it slides sideways instead. Ported from the ui-craft pass
/// on the prototype, where it took tag collisions from 28 of 80 orbit positions to 0.
/// </summary>
public static class TagLayout
{
    /// <summary>Tags never rise above this line; the viewport caption and toolbar live above it.</summary>
    public const float TopBand = 58;

    /// <summary>A pointer-sized tag's height; the viewport passes its measured height (44 px touch tags on phones).</summary>
    public const float Height = 24;
    public const float Gap = 4;

    /// <summary>Default distance between a unit's anchor and the bottom of its tag.</summary>
    public const float LeaderLength = 16;

    /// <summary>Places tags of the widths requested, all <paramref name="height"/> tall.</summary>
    public static ImmutableArray<TagPlacement> Solve(IReadOnlyList<TagRequest> requests, float viewportWidth, float height = Height)
    {
        var tags = requests
            .Select(r => new Mutable(r.Unit, r.Anchor, r.Anchor.X, r.Anchor.Y - LeaderLength, r.Width))
            .ToArray();

        var placed = new List<Mutable>(tags.Length);
        foreach (var c in tags.OrderByDescending(t => t.Bottom))
        {
            for (var k = 0; k < 12; k++)
            {
                var o = placed.FirstOrDefault(q => Overlaps(c, q, height));
                if (o is null) break;
                var up = o.Bottom - height - Gap;
                if (up - height >= TopBand) c.Bottom = up;
                else Slide(c, o);
            }

            if (c.Bottom - height < TopBand) c.Bottom = TopBand + height;

            for (var k = 0; k < 12; k++)
            {
                var o = placed.FirstOrDefault(q => Overlaps(c, q, height));
                if (o is null) break;
                Slide(c, o);
            }

            c.CenterX = Math.Clamp(c.CenterX, c.Width / 2 + 8, viewportWidth - c.Width / 2 - 8);
            placed.Add(c);
        }

        return [.. tags.Select(t => new TagPlacement(t.Unit, t.Anchor, t.CenterX, t.Bottom, t.Width, height))];
    }

    private static bool Overlaps(Mutable c, Mutable o, float height) =>
        MathF.Abs(c.CenterX - o.CenterX) < (c.Width + o.Width) / 2 + Gap
        && c.Bottom - height < o.Bottom + Gap
        && o.Bottom - height < c.Bottom + Gap;

    private static void Slide(Mutable c, Mutable o) =>
        c.CenterX = o.CenterX + (c.CenterX >= o.CenterX ? 1 : -1) * ((c.Width + o.Width) / 2 + Gap);

    private sealed class Mutable(UnitId unit, Vec2 anchor, float centerX, float bottom, float width)
    {
        public UnitId Unit { get; } = unit;
        public Vec2 Anchor { get; } = anchor;
        public float CenterX { get; set; } = centerX;
        public float Bottom { get; set; } = bottom;
        public float Width { get; } = width;
    }
}
