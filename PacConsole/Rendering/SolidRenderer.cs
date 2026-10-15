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
    SKColor Halo,
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
            Halo: C("SolidHaloColor"),
            Hairline: D("SolidStrokeHairline"),
            LitStroke: D("SolidStrokeLit"));
    }
}

/// <summary>What changes between frames: selection, alerts, each unit's lift and the clock.</summary>
public readonly record struct SolidFrame(
    UnitId? Selected,
    IReadOnlyCollection<UnitId> Alerting,
    IReadOnlyDictionary<UnitId, float>? Lift = null,
    double Seconds = 0,
    bool Details = true,
    UnitId? Hovered = null);

/// <summary>
/// Paints solids back to front. Orders the parts for the camera (lifted units raised), builds each
/// part's shape, fills paper, shades and top, strokes crease, bevel and outline, then draws the
/// part's line work. A lifted unit is drawn translated up the screen, and its pad gets a soft
/// shadow. Paints, path effects and the scratch path are created once and reused.
/// </summary>
public sealed class SolidRenderer : IDisposable
{
    /// <summary>Lift at which a unit is fully raised, in world units (the figure's LIFT).</summary>
    public const float FullLift = 10;

    private readonly SKPath _path = new() { FillType = SKPathFillType.Winding };
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _line = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPaint _halo = new() { Style = SKPaintStyle.Fill, IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4) };
    private readonly SKPathEffect _alertDash = SKPathEffect.CreateDash([4, 3], 0);

    public required SolidPalette Palette { get; init; }

    /// <summary>Paints <paramref name="parts"/> back to front. Lit parts of the selected unit take the accent; of an alerting unit, a dashed alert outline.</summary>
    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame)
    {
        float LiftOf(UnitId? unit) => unit is { } u && frame.Lift is not null && frame.Lift.TryGetValue(u, out var l) ? l : 0;

        var volumes = new List<Volume>(parts.Count);
        foreach (var part in parts) volumes.Add(part.Volume.Raised(LiftOf(part.Unit)));
        var order = DepthOrder.Sort(volumes, projection);

        foreach (var index in order)
        {
            var part = parts[index];
            var lift = LiftOf(part.Unit);
            var lit = part.Lit && part.Unit is { } u && u == frame.Selected;
            var alert = !lit && part.Lit && part.Unit is { } a && frame.Alerting.Contains(a);
            var hovered = !lit && part.Lit && part.Unit is { } h && h == frame.Hovered;

            if (lift > 0)
            {
                canvas.Save();
                canvas.Translate(0, projection.Rise(lift));
            }
            DrawShape(canvas, part.Build(projection), part.Edge, part.Crease, lit, alert, hovered);
            if (frame.Details && part.Details is { } details) DrawDetails(canvas, details(projection, frame.Seconds), frame.Seconds);
            if (lift > 0) canvas.Restore();

            if (part.PadOf is { } padUnit && LiftOf(padUnit) is var raised and > 0) DrawHalo(canvas, part.Volume, raised, projection);
        }
    }

    /// <summary>A soft shadow on the pad under a lifted unit, darker the higher it lifts (alpha 0.8 x lift / full).</summary>
    private void DrawHalo(SKCanvas canvas, Volume pad, float lift, OrbitProjection p)
    {
        var minX = pad.Footprint.Min(q => q.X) + 10;
        var maxX = pad.Footprint.Max(q => q.X) - 10;
        var minZ = pad.Footprint.Min(q => q.Z) + 10;
        var maxZ = pad.Footprint.Max(q => q.Z) - 10;
        var ring = new Plan(minX, minZ, maxX - minX, maxZ - minZ, 6).Ring(6);
        _path.Rewind();
        AddPolygon([.. ring.Select(q => p.Project(new(q.X, pad.Y1, q.Z)))]);
        _halo.Color = Palette.Halo.WithAlpha((byte)(Palette.Halo.Alpha * 0.8f * Math.Clamp(lift / FullLift, 0, 1)));
        canvas.DrawPath(_path, _halo);
    }

    private void DrawDetails(SKCanvas canvas, IEnumerable<DetailLine> details, double seconds)
    {
        foreach (var d in details)
        {
            switch (d.Kind)
            {
                case DetailKind.Dot:
                    _fill.Color = ToneColor(d.Tone);
                    canvas.DrawCircle(d.Points[0].X, d.Points[0].Y, 0.9f, _fill);
                    break;
                case DetailKind.Flow:
                    // Dashes move along the line at 12 px/s, so the direction of flow reads at a glance.
                    using (var dash = SKPathEffect.CreateDash([3, 5], (float)(-(seconds * 12) % 8)))
                    {
                        _line.PathEffect = dash;
                        StrokeLine(canvas, d.Points, Palette.Leader, 1);
                        _line.PathEffect = null;
                    }
                    break;
                default:
                    StrokeLine(canvas, d.Points, ToneColor(d.Tone), Palette.Hairline);
                    break;
            }
        }
    }

    private SKColor ToneColor(EdgeTone tone) => tone switch
    {
        EdgeTone.Hi => Palette.Hi,
        EdgeTone.Lo => Palette.Lo,
        EdgeTone.Faint => Palette.Faint,
        _ => Palette.Mid,
    };

    private void StrokeLine(SKCanvas canvas, Vec2[] points, SKColor color, float width)
    {
        if (points.Length < 2) return;
        _path.Rewind();
        _path.MoveTo(points[0].X, points[0].Y);
        for (var i = 1; i < points.Length; i++) _path.LineTo(points[i].X, points[i].Y);
        _line.Color = color;
        _line.StrokeWidth = width;
        canvas.DrawPath(_path, _line);
    }

    private void DrawShape(SKCanvas canvas, SolidShape shape, EdgeTone edge, bool crease, bool lit, bool alert, bool hovered = false)
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
        if (alert)
        {
            // Alert is dashed as well as coral, so it never reads as the solid amber selection.
            _line.PathEffect = _alertDash;
            Stroke(canvas, shape.Outline, pal.Alert, pal.LitStroke);
            _line.PathEffect = null;
            return;
        }
        // Hover brightens the outline to the hi tone; selection is the amber lit outline.
        Stroke(canvas, shape.Outline, lit ? pal.Lit : hovered ? pal.Hi : ToneColor(edge), lit || hovered ? pal.LitStroke : pal.Hairline);
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
        _halo.MaskFilter?.Dispose();
        _halo.Dispose();
        _alertDash.Dispose();
    }
}
