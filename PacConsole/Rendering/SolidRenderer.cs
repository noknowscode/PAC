using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>Colours and widths for painting solids, read once (never inside RenderOverride).</summary>
public sealed record SolidPalette(
    SKColor Paper,
    SKColor[] Shades,
    SKColor Top,
    SKColor LitFace,
    SKColor LitShade,
    SKColor LitTop,
    SKColor Hi,
    SKColor Mid,
    SKColor Lo,
    SKColor Faint,
    SKColor Lit,
    float Hairline)
{
    /// <summary>The reference figure's dark palette (the Anatomy kit's CSS variables).</summary>
    public static readonly SolidPalette Figure = new(
        Paper: SKColor.Parse("#131313"),
        Shades: [SKColor.Parse("#0f0f0f"), SKColor.Parse("#121212"), SKColor.Parse("#151515"), SKColor.Parse("#181818")],
        Top: SKColor.Parse("#1c1c1c"),
        LitFace: SKColor.Parse("#1b1b1b"),
        LitShade: SKColor.Parse("#1d1d1d"),
        LitTop: SKColor.Parse("#262626"),
        Hi: SKColor.Parse("#d4d4d4"),
        Mid: SKColor.Parse("#6a6a6a"),
        Lo: SKColor.Parse("#3e3e3e"),
        Faint: SKColor.Parse("#2a2a2a"),
        Lit: SKColors.White,
        Hairline: 0.6f);
}

/// <summary>
/// Paints solids back to front. Orders the parts for the camera, builds each part's shape, then
/// fills paper, shades and top and strokes crease, bevel and outline. Paints and the scratch path
/// are created once and reused for every part and frame.
/// </summary>
public sealed class SolidRenderer : IDisposable
{
    private readonly SKPath _path = new() { FillType = SKPathFillType.Winding };
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _line = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

    public SolidPalette Palette { get; set; } = SolidPalette.Figure;

    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, UnitId? selected)
    {
        var order = DepthOrder.Sort(parts.Select(p => p.Volume).ToList(), projection);
        foreach (var index in order)
        {
            var part = parts[index];
            DrawShape(canvas, part.Build(projection), part.Edge, part.Crease, part.Lit && part.Unit is { } u && u == selected);
        }
    }

    private void DrawShape(SKCanvas canvas, SolidShape shape, EdgeTone edge, bool crease, bool lit)
    {
        var pal = Palette;
        Fill(canvas, shape.Fill, lit ? pal.LitFace : pal.Paper);
        for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.ShadeFaces[s], lit ? pal.LitShade : pal.Shades[s]);
        Fill(canvas, shape.Top, lit ? pal.LitTop : pal.Top);
        if (crease)
        {
            Stroke(canvas, shape.Crease, pal.Faint);
            Stroke(canvas, shape.Bevel, pal.Faint);
        }
        Stroke(canvas, shape.Outline, lit ? pal.Lit : edge switch
        {
            EdgeTone.Hi => pal.Hi,
            EdgeTone.Lo => pal.Lo,
            EdgeTone.Faint => pal.Faint,
            _ => pal.Mid,
        });
    }

    private void Fill(SKCanvas canvas, Vec2[] polygon, SKColor color)
    {
        if (polygon.Length < 3) return;
        _path.Rewind();
        AddPolygon(polygon);
        _fill.Color = color;
        canvas.DrawPath(_path, _fill);
    }

    private void Fill(SKCanvas canvas, List<Vec2[]> polygons, SKColor color)
    {
        if (polygons.Count == 0) return;
        _path.Rewind();
        foreach (var polygon in polygons) AddPolygon(polygon);
        _fill.Color = color;
        canvas.DrawPath(_path, _fill);
    }

    private void Stroke(SKCanvas canvas, List<Vec2[]> lines, SKColor color)
    {
        if (lines.Count == 0) return;
        _path.Rewind();
        foreach (var line in lines)
        {
            if (line.Length < 2) continue;
            _path.MoveTo(line[0].X, line[0].Y);
            for (var i = 1; i < line.Length; i++) _path.LineTo(line[i].X, line[i].Y);
        }
        _line.Color = color;
        _line.StrokeWidth = Palette.Hairline;
        canvas.DrawPath(_path, _line);
    }

    private void AddPolygon(Vec2[] polygon)
    {
        _path.MoveTo(polygon[0].X, polygon[0].Y);
        for (var i = 1; i < polygon.Length; i++) _path.LineTo(polygon[i].X, polygon[i].Y);
        _path.Close();
    }

    public void Dispose()
    {
        _path.Dispose();
        _fill.Dispose();
        _line.Dispose();
    }
}
