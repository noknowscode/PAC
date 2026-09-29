using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>
/// Draws the plant wireframe. Shared by the main viewport, the camera-preset thumbnails and the
/// unit preview. Paints and paths are allocated once and reused every frame; only the segment
/// endpoints change.
/// </summary>
public sealed class WireframeRenderer : IDisposable
{
    private static readonly SKColor Cyan = new(150, 226, 255);
    private static readonly SKColor CyanDim = new(110, 210, 255);
    private static readonly SKColor Accent = new(0xF5, 0xA8, 0x3B);
    private static readonly SKColor Alert = new(0xFF, 0x6F, 0x61);

    private readonly SKPath _grid = new(), _base = new(), _warn = new(), _sel = new(), _leaders = new();

    private readonly SKPaint _gridPaint = Stroke(CyanDim.WithAlpha(41), 1);
    private readonly SKPaint _baseGlowPaint = Stroke(CyanDim.WithAlpha(31), 4);
    private readonly SKPaint _basePaint = Stroke(Cyan.WithAlpha(209), 1);
    private readonly SKPaint _warnGlowPaint = Stroke(Alert.WithAlpha(51), 5);
    private readonly SKPaint _warnPaint = Stroke(Alert, 1.2f, dashed: true);
    private readonly SKPaint _selGlowPaint = Stroke(Accent.WithAlpha(64), 6);
    private readonly SKPaint _selPaint = Stroke(Accent, 1.4f);
    private readonly SKPaint _leaderPaint = Stroke(Cyan.WithAlpha(153), 1);

    /// <summary>
    /// Draws the whole plant. <paramref name="strokeScale"/> thins every stroke for thumbnails.
    /// </summary>
    public void DrawPlant(
        SKCanvas canvas,
        OrbitProjection projection,
        PlantGeometry plant,
        UnitId? selected,
        IReadOnlyCollection<UnitId> alerting,
        IReadOnlyList<TagPlacement>? tags = null,
        float strokeScale = 1f,
        bool glow = true)
    {
        _grid.Reset(); _base.Reset(); _warn.Reset(); _sel.Reset(); _leaders.Reset();

        Append(_grid, plant.Grid, projection);
        Append(_base, plant.Structure, projection);
        foreach (var (id, segments) in plant.Units)
        {
            var target = id == selected ? _sel : alerting.Contains(id) ? _warn : _base;
            Append(target, segments, projection);
        }

        if (tags is not null)
        {
            foreach (var t in tags)
            {
                _leaders.MoveTo(t.Anchor.X, t.Anchor.Y);
                _leaders.LineTo(t.CenterX, t.Bottom);
                _leaders.AddRect(new SKRect(t.Anchor.X - 2, t.Anchor.Y - 2, t.Anchor.X + 2, t.Anchor.Y + 2));
            }
        }

        Draw(canvas, _grid, _gridPaint, strokeScale);
        if (glow) Draw(canvas, _base, _baseGlowPaint, strokeScale);
        Draw(canvas, _base, _basePaint, strokeScale);
        if (glow) Draw(canvas, _warn, _warnGlowPaint, strokeScale);
        Draw(canvas, _warn, _warnPaint, strokeScale);
        if (glow) Draw(canvas, _sel, _selGlowPaint, strokeScale);
        Draw(canvas, _sel, _selPaint, strokeScale);
        Draw(canvas, _leaders, _leaderPaint, 1);
    }

    /// <summary>Draws one unit in the accent, with its glow, for the unit preview.</summary>
    public void DrawUnit(SKCanvas canvas, OrbitProjection projection, ImmutableArray<Segment3> segments, float strokeScale = 0.8f)
    {
        _sel.Reset();
        Append(_sel, segments, projection);
        Draw(canvas, _sel, _selGlowPaint, strokeScale * 0.5f);
        Draw(canvas, _sel, _selPaint, strokeScale);
    }

    private static void Append(SKPath path, ImmutableArray<Segment3> segments, OrbitProjection projection)
    {
        foreach (var s in segments)
        {
            var a = projection.Project(s.A);
            var b = projection.Project(s.B);
            path.MoveTo(a.X, a.Y);
            path.LineTo(b.X, b.Y);
        }
    }

    private static void Draw(SKCanvas canvas, SKPath path, SKPaint paint, float strokeScale)
    {
        if (path.IsEmpty) return;
        var width = paint.StrokeWidth;
        paint.StrokeWidth = width * strokeScale;
        canvas.DrawPath(path, paint);
        paint.StrokeWidth = width;
    }

    private static SKPaint Stroke(SKColor color, float width, bool dashed = false) => new()
    {
        Color = color,
        StrokeWidth = width,
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        PathEffect = dashed ? SKPathEffect.CreateDash([4, 2], 0) : null,
    };

    public void Dispose()
    {
        foreach (var p in new[] { _grid, _base, _warn, _sel, _leaders }) p.Dispose();
        foreach (var p in new[] { _gridPaint, _baseGlowPaint, _basePaint, _warnGlowPaint, _warnPaint, _selGlowPaint, _selPaint, _leaderPaint })
        {
            p.PathEffect?.Dispose();
            p.Dispose();
        }
    }
}
