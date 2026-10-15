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
    SKColor Alert,
    SKColor Leader,
    float Hairline,
    float LitStroke)
{
    /// <summary>The console palette from <c>Styles/Tokens.xaml</c> (the Solid* colours), so every plant drawing matches its card.</summary>
    public static SolidPalette FromResources(ResourceDictionary r)
    {
        SKColor C(string key) => r[key] switch
        {
            Windows.UI.Color c => new SKColor(c.R, c.G, c.B, c.A),
            SolidColorBrush b => new SKColor(b.Color.R, b.Color.G, b.Color.B, b.Color.A),
            _ => SKColors.Magenta,
        };
        float D(string key) => (float)(double)r[key];
        return new(
            Paper: C("SolidPaperColor"),
            Shades: [C("SolidShade0Color"), C("SolidShade1Color"), C("SolidShade2Color"), C("SolidShade3Color")],
            Top: C("SolidTopColor"),
            LitFace: C("SolidLitFaceColor"),
            LitShade: C("SolidLitShadeColor"),
            LitTop: C("SolidLitTopColor"),
            Hi: C("SolidLineHiColor"),
            Mid: C("SolidLineMidColor"),
            Lo: C("SolidLineLoColor"),
            Faint: C("SolidLineFaintColor"),
            Lit: C("AccentBrush"),
            Alert: C("AlertBrush"),
            Leader: C("SolidLeaderColor"),
            Hairline: D("SolidStrokeHairline"),
            LitStroke: D("SolidStrokeLit"));
    }
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

    public required SolidPalette Palette { get; init; }

    /// <summary>Paints <paramref name="parts"/> back to front. Lit parts of the selected unit take the accent; of an alerting unit, the alert colour.</summary>
    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, UnitId? selected, IReadOnlyCollection<UnitId>? alerting = null)
    {
        var order = DepthOrder.Sort(parts.Select(p => p.Volume).ToList(), projection);
        foreach (var index in order)
        {
            var part = parts[index];
            var lit = part.Lit && part.Unit is { } u && u == selected;
            var alert = !lit && part.Lit && part.Unit is { } a && alerting is not null && alerting.Contains(a);
            DrawShape(canvas, part.Build(projection), part.Edge, part.Crease, lit, alert);
        }
    }

    private void DrawShape(SKCanvas canvas, SolidShape shape, EdgeTone edge, bool crease, bool lit, bool alert)
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
        var outline = lit ? pal.Lit : alert ? pal.Alert : edge switch
        {
            EdgeTone.Hi => pal.Hi,
            EdgeTone.Lo => pal.Lo,
            EdgeTone.Faint => pal.Faint,
            _ => pal.Mid,
        };
        Stroke(canvas, shape.Outline, outline, lit || alert ? pal.LitStroke : pal.Hairline);
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

    private void Stroke(SKCanvas canvas, List<Vec2[]> lines, SKColor color, float? width = null)
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
        _line.StrokeWidth = width ?? Palette.Hairline;
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
