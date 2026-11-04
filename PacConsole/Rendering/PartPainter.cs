using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>Which part of a live part a draw covers: everything, its still body, or only its moving marks.</summary>
internal enum PartPass { All, Body, Motion }

/// <summary>Everything a part's drawing depends on besides the camera, palette and scale.</summary>
internal readonly record struct PartLook(bool Lit, bool Alert, bool Hovered, bool Faded, PlantSystem? Ink, bool Details);

/// <summary>
/// Inks one part the way a technical isometric is inked: fills, then fine interior lines, then a heavier silhouette;
/// a circuit's pipe in its own tints; the selection lit, an alert dashed, a hovered unit outlined, and the isolated
/// system in its ink with a soft halo, while parts outside it fade toward the paper. Paints and path effects are made
/// once and reused; paths come from <see cref="SolidGeometry"/>.
/// </summary>
internal sealed class PartPainter(SolidPalette palette, SolidGeometry geometry) : IDisposable
{
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _line = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPathEffect _alertDash = SKPathEffect.CreateDash([4, 3], 0);

    // System highlight: a soft halo behind each part of the isolated system, then its outline in the
    // system's ink. The blur is one cached mask filter; nothing here is built per frame.
    private readonly SKMaskFilter _glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
    private readonly SKPaint _glow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 3, StrokeJoin = SKStrokeJoin.Round };

    /// <summary>How far a part outside the chosen system fades toward the paper (0 = none, 1 = gone).</summary>
    private const float SystemFade = 0.78f;

    private float _fade;

    /// <summary>The isolated system's ink for the part being drawn, or null when it is not highlighted.</summary>
    private SKColor? _systemInk;

    /// <summary>The ground shadow, opaque in one pass so overlaps never darken.</summary>
    public void DrawShadow(SKCanvas canvas)
    {
        if (geometry.Shadow is not { } shadow) return;
        _fill.Color = palette.Shadow;
        canvas.DrawPath(shadow, _fill);
    }

    public void DrawPart(SKCanvas canvas, SolidPart part, int index, OrbitProjection projection, SolidFrame frame, float lift, PartPass pass = PartPass.All)
    {
        var look = LookOf(part, index, frame);
        _fade = look.Faded ? SystemFade : 0;
        _systemInk = look.Ink is { } system ? palette.Systems[system] : null;

        if (lift > 0)
        {
            canvas.Save();
            canvas.Translate(0, projection.Rise(lift));
        }
        if (pass != PartPass.Motion) DrawShape(canvas, geometry.PathsOf(index, part.Crease), part.Edge, part.Crease, look.Lit, look.Alert, look.Hovered, part.Circuit);
        if (frame.Details && part.Details is { } details)
        {
            // A still part's lines keep their paths for the camera's life; a moving part's are built per draw.
            if (geometry.Details[index] is { } still) DrawDetails(canvas, still, geometry.DetailPathsOf(index, still.Length), frame.Seconds, part.AnimatedDetails, pass);
            else DrawDetails(canvas, details(projection, frame.Seconds), null, frame.Seconds, part.AnimatedDetails, pass);
        }
        if (lift > 0) canvas.Restore();
        _fade = 0;
        _systemInk = null;
    }

    public PartLook LookOf(SolidPart part, int index, SolidFrame frame)
    {
        var lit = part.Lit && part.Unit is { } u && u == frame.Selected;
        var alert = !lit && part.Lit && part.Unit is { } a && frame.Alerting.Contains(a);
        var hovered = !lit && part.Lit && part.Unit is { } h && h == frame.Hovered;
        var inSystem = geometry.InSystem(index, frame.System, frame.Alerting);
        // Outside the chosen system a part fades toward the paper; the selected unit always stays inked.
        var faded = !(part.Unit is { } s && s == frame.Selected || inSystem);
        // Inside an isolated system (not the ground, the selection or an alert), a part takes the system's ink.
        var ink = frame.System != PlantSystem.All && inSystem && !lit && !alert && !SolidGeometry.IsStand(part) && palette.Systems.ContainsKey(frame.System)
            ? frame.System : (PlantSystem?)null;
        return new(lit, alert, hovered, faded, ink, frame.Details);
    }

    /// <summary>A colour as the current part draws it: faded toward the paper when it is outside the system.</summary>
    private SKColor Ink(SKColor c) => _fade == 0 ? c : Lerp(c, palette.Paper, _fade);

    /// <param name="paths">The cache for a still part's lines (one slot per line, filled on first draw), or null to build each line for this draw.</param>
    private void DrawDetails(SKCanvas canvas, IEnumerable<DetailLine> details, SKPath?[]? paths, double seconds, bool animated, PartPass pass)
    {
        var k = -1;
        foreach (var d in details)
        {
            k++;
            // Flow marks move, and every line of an animated part (a fan's blades) does.
            var moves = animated || d.Kind == DetailKind.Flow;
            if (pass == PartPass.Body && moves || pass == PartPass.Motion && !moves) continue;
            switch (d.Kind)
            {
                case DetailKind.Dot:
                    _fill.Color = Ink(ToneColor(d.Tone));
                    canvas.DrawCircle(d.Points[0].X, d.Points[0].Y, 0.9f, _fill);
                    break;
                case DetailKind.Flow:
                    // Dashes move along the line at 12 px/s, so the direction of flow reads at a glance.
                    // Isolated, a circuit's flow runs in the system's ink at 24 px/s.
                    var fast = _systemInk is not null;
                    _line.PathEffect = fast ? FastFlowDash(seconds) : FlowDash(seconds);
                    StrokeLine(canvas, d.Points, paths, k, _systemInk ?? palette.Leader, fast ? 1.4f : 1);
                    _line.PathEffect = null;
                    break;
                default:
                    StrokeLine(canvas, d.Points, paths, k, ToneColor(d.Tone), palette.Hairline);
                    break;
            }
        }
    }

    private SKPathEffect? _flowDash;
    private double _flowDashSeconds = double.NaN;

    /// <summary>
    /// One dash effect per frame, shared by every flow mark. Creating one per pipe (about 50 a frame
    /// with the thermal cycle's runs) kept the finalizer thread busy enough to starve frames.
    /// </summary>
    private SKPathEffect FlowDash(double seconds)
    {
        if (_flowDash is not null && seconds == _flowDashSeconds) return _flowDash;
        _flowDash?.Dispose();
        _flowDashSeconds = seconds;
        return _flowDash = SKPathEffect.CreateDash([3, 5], (float)(-(seconds * 12) % 8));
    }

    private SKPathEffect? _fastFlowDash;
    private double _fastFlowDashSeconds = double.NaN;

    /// <summary>The isolated circuit's dashes at 24 px/s: a second cached effect, so the two speeds in one frame never re-create each other.</summary>
    private SKPathEffect FastFlowDash(double seconds)
    {
        if (_fastFlowDash is not null && seconds == _fastFlowDashSeconds) return _fastFlowDash;
        _fastFlowDash?.Dispose();
        _fastFlowDashSeconds = seconds;
        return _fastFlowDash = SKPathEffect.CreateDash([3, 5], (float)(-(seconds * 24) % 8));
    }

    private SKColor ToneColor(EdgeTone tone) => tone switch
    {
        EdgeTone.Hi => palette.Hi,
        EdgeTone.Lo => palette.Lo,
        EdgeTone.Faint => palette.Faint,
        _ => palette.Mid,
    };

    private void StrokeLine(SKCanvas canvas, Vec2[] points, SKPath?[]? paths, int index, SKColor color, float width)
    {
        if (points.Length < 2) return;
        _line.Color = Ink(color);
        _line.StrokeWidth = width;
        if (paths is not null)
        {
            canvas.DrawPath(paths[index] ??= geometry.LinePath(points), _line);
            return;
        }
        using var path = geometry.LinePath(points);
        canvas.DrawPath(path, _line);
    }

    private void DrawShape(SKCanvas canvas, ShapePaths shape, EdgeTone edge, bool crease, bool lit, bool alert, bool hovered = false, Circuit circuit = Circuit.None)
    {
        var pal = palette;
        if (_systemInk is { } halo && shape.Outline is { } outline)
        {
            _glow.Color = halo.WithAlpha(0x73);
            _glow.MaskFilter = _glowBlur;
            canvas.DrawPath(outline, _glow);
            _glow.MaskFilter = null;
        }
        if (circuit != Circuit.None && pal.Circuits.TryGetValue(circuit, out var tint))
        {
            // A circuit's pipe: shades run from its deep tone to its base, the top a step lighter than
            // the base, and the outline in the circuit's own ink, so a run reads end to end.
            Fill(canvas, shape.Fill, tint.Base);
            for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.Shades[s], Lerp(tint.Deep, tint.Base, s / 3f));
            Fill(canvas, shape.Top, Lerp(tint.Base, tint.Deep, -0.35f));
            Stroke(canvas, shape.Outline, _systemInk ?? Lerp(tint.Deep, pal.Silhouette, 0.5f), _systemInk is null ? pal.SilhouetteStroke : pal.LitStroke);
            return;
        }
        Fill(canvas, shape.Fill, lit ? pal.LitFace : pal.Paper);
        for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.Shades[s], lit ? pal.LitShade : pal.Shades[s]);
        Fill(canvas, shape.Top, lit ? pal.LitTop : pal.Top);
        if (crease)
        {
            Stroke(canvas, shape.Crease, pal.Faint, pal.Hairline * 0.75f);
            Stroke(canvas, shape.Bevel, pal.Faint, pal.Hairline * 0.75f);
        }
        if (alert)
        {
            // Alert is dashed as well as coral, so it never reads as the solid amber selection.
            _line.PathEffect = _alertDash;
            Stroke(canvas, shape.Outline, pal.Alert, pal.LitStroke);
            _line.PathEffect = null;
            return;
        }
        // Two line weights, as an inked isometric: equipment (hi / mid edges) gets the ink silhouette,
        // the stand and fittings (lo / faint) a lighter hairline. Hover inks the outline in the hi tone;
        // selection is the orange lit outline.
        var (ink, width) = edge is EdgeTone.Hi or EdgeTone.Mid
            ? (pal.Silhouette, pal.SilhouetteStroke)
            : (edge == EdgeTone.Lo ? pal.Mid : pal.Lo, pal.Hairline);
        if (_systemInk is { } sys && !hovered)
        {
            Stroke(canvas, shape.Outline, sys, pal.LitStroke);
            return;
        }
        Stroke(canvas, shape.Outline, lit ? pal.Lit : hovered ? pal.Hi : ink, lit || hovered ? pal.LitStroke : width);
    }

    /// <summary>Linear blend from a to b (t outside 0..1 extrapolates, clamped per channel).</summary>
    public static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        static byte Mix(byte x, byte y, float t) => (byte)Math.Clamp(MathF.Round(x + (y - x) * t), 0, 255);
        return new(Mix(a.Red, b.Red, t), Mix(a.Green, b.Green, t), Mix(a.Blue, b.Blue, t), Mix(a.Alpha, b.Alpha, t));
    }

    private void Fill(SKCanvas canvas, SKPath? path, SKColor color)
    {
        if (path is null) return;
        _fill.Color = Ink(color);
        canvas.DrawPath(path, _fill);
    }

    private void Stroke(SKCanvas canvas, SKPath? path, SKColor color, float? width = null)
    {
        if (path is null) return;
        _line.Color = Ink(color);
        _line.StrokeWidth = width ?? palette.Hairline;
        canvas.DrawPath(path, _line);
    }

    public void Dispose()
    {
        _fill.Dispose();
        _line.Dispose();
        _alertDash.Dispose();
        _glow.Dispose();
        _glowBlur.Dispose();
        _flowDash?.Dispose();
        _fastFlowDash?.Dispose();
    }
}
