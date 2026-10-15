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
    SKColor Shadow,
    SKColor Silhouette,
    float Hairline,
    float SilhouetteStroke,
    float LitStroke)
{
    /// <summary>Base and deep tone per pipe circuit (steam, water, cooling, fuel).</summary>
    public IReadOnlyDictionary<Circuit, (SKColor Base, SKColor Deep)> Circuits { get; init; } = new Dictionary<Circuit, (SKColor, SKColor)>();

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
            Shadow: C("SolidShadowColor"),
            Silhouette: C("SolidSilhouetteColor"),
            Hairline: D("SolidStrokeHairline"),
            SilhouetteStroke: D("SolidStrokeSilhouette"),
            LitStroke: D("SolidStrokeLit"))
        {
            Circuits = new Dictionary<Circuit, (SKColor, SKColor)>
            {
                [Circuit.Steam] = (C("SolidSteamColor"), C("CircuitSteamBrush")),
                [Circuit.Water] = (C("SolidWaterColor"), C("CircuitWaterBrush")),
                [Circuit.Cooling] = (C("SolidCoolingColor"), C("CircuitCoolingBrush")),
                [Circuit.Fuel] = (C("SolidFuelColor"), C("CircuitFuelBrush")),
            },
        };
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
/// Paints solids back to front, the way a technical isometric is inked: the stand first, then one
/// flat ground shadow for everything standing on it, then each part (fills, then fine interior lines,
/// then a heavier silhouette). Shadows fall away from a light at the camera's upper left, so they
/// read the same at every yaw; a lifted unit's shadow slides out from under it. Paints, path effects
/// and paths are created once and reused.
/// </summary>
public sealed class SolidRenderer : IDisposable
{
    /// <summary>Lift at which a unit is fully raised, in world units (the figure's LIFT).</summary>
    public const float FullLift = 10;

    private readonly SKPath _path = new() { FillType = SKPathFillType.Winding };
    private readonly SKPaint _fill = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _line = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _shadow = new() { FillType = SKPathFillType.Winding };
    private readonly SKPathEffect _alertDash = SKPathEffect.CreateDash([4, 3], 0);

    public required SolidPalette Palette { get; init; }

    // Shapes depend only on the camera; a lift is a screen translation. So a still camera with a
    // lifting unit, turning fans or moving flow rebuilds nothing, and re-sorts only when a lift moves.
    private IReadOnlyList<SolidPart>? _cachedParts;
    private (float, float, float, float, float, float) _cachedCamera;
    private SolidShape[] _shapes = [];
    private DetailLine[]?[] _details = [];
    private int[] _order = [];
    private float[] _orderLift = [];

    /// <summary>Paints <paramref name="parts"/> back to front. Lit parts of the selected unit take the accent; of an alerting unit, a dashed alert outline.</summary>
    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame)
    {
        float LiftOf(UnitId? unit) => unit is { } u && frame.Lift is not null && frame.Lift.TryGetValue(u, out var l) ? l : 0;

        var camera = (projection.SinYaw, projection.CosYaw, projection.SinElevation, projection.Scale, projection.CenterX, projection.CenterY);
        var cameraChanged = !SameParts(parts, _cachedParts) || camera != _cachedCamera;
        if (cameraChanged)
        {
            _cachedParts = parts;
            _cachedCamera = camera;
            _shapes = new SolidShape[parts.Count];
            _details = new DetailLine[]?[parts.Count];
            _live = new bool[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                _shapes[i] = parts[i].Build(projection);
                if (parts[i].Details is { } d && !parts[i].AnimatedDetails) _details[i] = [.. d(projection, 0)];
                // Live parts change with time (fan blades, moving flow marks) and are drawn every frame.
                _live[i] = parts[i].AnimatedDetails || (_details[i]?.Any(l => l.Kind == DetailKind.Flow) ?? false);
            }
        }

        var lifts = new float[parts.Count];
        for (var i = 0; i < parts.Count; i++) lifts[i] = LiftOf(parts[i].Unit);
        if (cameraChanged || !lifts.AsSpan().SequenceEqual(_orderLift))
        {
            var volumes = new List<Volume>(parts.Count);
            for (var i = 0; i < parts.Count; i++) volumes.Add(parts[i].Volume.Raised(lifts[i]));
            _order = DepthOrder.Sort(volumes, projection);
            _orderLift = lifts;
        }
        var order = _order;
        if (cameraChanged || lifts.AsSpan().SequenceEqual(_shadowLift) is false) BuildShadows(parts, lifts, projection);

        // The stand (plate, pads, strip) lies under everything, so it goes first; then the shadows
        // that fall on it; then every other part in depth order. -1 stands for the shadow pass.
        var sequence = new List<int>(order.Length + 1);
        foreach (var index in order) if (IsGround(parts[index])) sequence.Add(index);
        sequence.Add(-1);
        foreach (var index in order) if (!IsGround(parts[index])) sequence.Add(index);

        void Op(SKCanvas c, int index)
        {
            if (index < 0)
            {
                _fill.Color = Palette.Shadow;
                c.DrawPath(_shadow, _fill);
            }
            else
            {
                DrawPart(c, parts[index], index, projection, frame, LiftOf(parts[index].Unit));
            }
        }

        // A moving camera or lift draws directly: recording would only add cost to a frame that is
        // thrown away. Once the view holds, static runs are recorded once and replayed, so a still
        // plant costs a few picture replays plus its live parts (trace: ~2,500 paths a frame otherwise).
        var key = new StillKey(camera, frame.Selected, frame.Hovered, AlertMask(frame.Alerting), frame.Details, Palette);
        var moving = cameraChanged || !lifts.AsSpan().SequenceEqual(_stillLift);
        _stillLift = lifts;
        if (moving)
        {
            ClearPictures();
            foreach (var index in sequence) Op(canvas, index);
            return;
        }
        if (_segments.Count == 0 || key != _stillKey)
        {
            ClearPictures();
            _stillKey = key;
            var bounds = canvas.LocalClipBounds;
            SKPictureRecorder? recorder = null;
            SKCanvas? recording = null;
            void Flush()
            {
                if (recorder is null) return;
                _segments.Add((recorder.EndRecording(), -2));
                recorder.Dispose();
                recorder = null;
                recording = null;
            }
            foreach (var index in sequence)
            {
                if (index >= 0 && _live[index])
                {
                    Flush();
                    _segments.Add((null, index));
                    continue;
                }
                if (recorder is null)
                {
                    recorder = new SKPictureRecorder();
                    recording = recorder.BeginRecording(bounds);
                }
                Op(recording!, index);
            }
            Flush();
        }
        foreach (var (picture, index) in _segments)
        {
            if (picture is not null) canvas.DrawPicture(picture);
            else Op(canvas, index);
        }
    }

    private bool[] _live = [];
    private float[] _stillLift = [];
    private StillKey _stillKey;
    private readonly List<(SKPicture? Picture, int Part)> _segments = [];

    /// <summary>What a recorded still frame depends on besides the camera and lifts.</summary>
    private readonly record struct StillKey(
        (float, float, float, float, float, float) Camera,
        UnitId? Selected,
        UnitId? Hovered,
        int Alerts,
        bool Details,
        SolidPalette? Palette);

    private static int AlertMask(IReadOnlyCollection<UnitId> alerting)
    {
        var mask = 0;
        foreach (var u in alerting) mask |= 1 << (int)u;
        return mask;
    }

    private void ClearPictures()
    {
        foreach (var (picture, _) in _segments) picture?.Dispose();
        _segments.Clear();
    }

    private float[] _shadowLift = [];

    /// <summary>
    /// The same parts list as last frame. Callers pass <c>ImmutableArray</c>s, a struct boxed afresh
    /// on every call, so reference equality alone always failed and every frame rebuilt and re-sorted
    /// every shape as if the camera had moved (Release trace, 2026-10-15: still plant at 12 fps).
    /// </summary>
    private static bool SameParts(IReadOnlyList<SolidPart> a, IReadOnlyList<SolidPart>? b) =>
        ReferenceEquals(a, b) || (a is ImmutableArray<SolidPart> x && b is ImmutableArray<SolidPart> y && x == y);

    /// <summary>Flat on the plate (plate, feet, pads, strip, label): no higher than a pad. Piers and sleepers stand up and cast shadows.</summary>
    private static bool IsGround(SolidPart part) =>
        part.Name.StartsWith("stand.", StringComparison.Ordinal) && part.Volume.Y1 <= PlantSolids.PadHeight + 0.01f;

    /// <summary>
    /// One path holding every part's ground shadow: the hull of its base and its top, each pushed
    /// away from the light in proportion to its height above the surface it stands on (a unit's pad,
    /// or the plate). Drawn opaque in a single pass, so overlaps never darken.
    /// </summary>
    private void BuildShadows(IReadOnlyList<SolidPart> parts, float[] lifts, OrbitProjection p)
    {
        _shadowLift = lifts;
        _shadow.Rewind();
        var padTop = parts.FirstOrDefault(q => q.PadOf is not null)?.Volume.Y1 ?? 0;
        // Light from the viewer's upper left: shadows fall to the right and toward the viewer at every
        // yaw, as in an inked isometric. The plan direction comes from the screen gradients of world
        // X and Z, so it needs no knowledge of the projection's sign conventions.
        var o = p.Project(new(0, 0, 0));
        var ax = p.Project(new(1, 0, 0));
        var az = p.Project(new(0, 0, 1));
        var (rx, rz) = Unit(ax.X - o.X, az.X - o.X);   // plan direction that moves right on screen
        var (tx, tz) = Unit(ax.Y - o.Y, az.Y - o.Y);   // plan direction that moves down (toward the viewer)
        var (dx, dz) = Unit(0.75f * rx + 0.65f * tx, 0.75f * rz + 0.65f * tz);
        dx *= ShadowLength;
        dz *= ShadowLength;
        var points = new List<Vec2>(64);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (IsGround(part)) continue;
            var v = part.Volume;
            var ground = part.Unit is null ? 0 : padTop;
            var low = MathF.Max(0, v.Y0 + lifts[i] - ground);
            var high = MathF.Max(0, v.Y1 + lifts[i] - ground);
            if (high <= 0) continue;
            points.Clear();
            foreach (var q in v.Bottom) points.Add(new(q.X + dx * low, q.Z + dz * low));
            foreach (var q in v.Top) points.Add(new(q.X + dx * high, q.Z + dz * high));
            var hull = SolidBuilder.ConvexHull(points);
            if (hull.Length < 3) continue;
            var first = p.Project(new(hull[0].X, ground, hull[0].Y));
            _shadow.MoveTo(first.X, first.Y);
            for (var k = 1; k < hull.Length; k++)
            {
                var s = p.Project(new(hull[k].X, ground, hull[k].Y));
                _shadow.LineTo(s.X, s.Y);
            }
            _shadow.Close();
        }
    }

    private static (float X, float Z) Unit(float x, float z)
    {
        var length = MathF.Sqrt(x * x + z * z);
        return length < 1e-5f ? (0, 0) : (x / length, z / length);
    }

    /// <summary>Shadow length per unit of height (a sun about 60 degrees up).</summary>
    private const float ShadowLength = 0.55f;

    private void DrawPart(SKCanvas canvas, SolidPart part, int index, OrbitProjection projection, SolidFrame frame, float lift)
    {
        var lit = part.Lit && part.Unit is { } u && u == frame.Selected;
        var alert = !lit && part.Lit && part.Unit is { } a && frame.Alerting.Contains(a);
        var hovered = !lit && part.Lit && part.Unit is { } h && h == frame.Hovered;

        if (lift > 0)
        {
            canvas.Save();
            canvas.Translate(0, projection.Rise(lift));
        }
        DrawShape(canvas, _shapes[index], part.Edge, part.Crease, lit, alert, hovered, part.Circuit);
        if (frame.Details && part.Details is { } details)
        {
            DrawDetails(canvas, _details[index] ?? details(projection, frame.Seconds), frame.Seconds);
        }
        if (lift > 0) canvas.Restore();
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
                    _line.PathEffect = FlowDash(seconds);
                    StrokeLine(canvas, d.Points, Palette.Leader, 1);
                    _line.PathEffect = null;
                    break;
                default:
                    StrokeLine(canvas, d.Points, ToneColor(d.Tone), Palette.Hairline);
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

    private void DrawShape(SKCanvas canvas, SolidShape shape, EdgeTone edge, bool crease, bool lit, bool alert, bool hovered = false, Circuit circuit = Circuit.None)
    {
        var pal = Palette;
        if (circuit != Circuit.None && pal.Circuits.TryGetValue(circuit, out var tint))
        {
            // A circuit's pipe: shades run from its deep tone to its base, the top a step lighter than
            // the base, and the outline in the circuit's own ink, so a run reads end to end.
            Fill(canvas, shape.Fill, tint.Base);
            for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.ShadeFaces[s], Lerp(tint.Deep, tint.Base, s / 3f));
            Fill(canvas, shape.Top, Lerp(tint.Base, tint.Deep, -0.35f));
            Stroke(canvas, shape.Outline, Lerp(tint.Deep, pal.Silhouette, 0.5f), pal.SilhouetteStroke);
            return;
        }
        Fill(canvas, shape.Fill, lit ? pal.LitFace : pal.Paper);
        for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.ShadeFaces[s], lit ? pal.LitShade : pal.Shades[s]);
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
        Stroke(canvas, shape.Outline, lit ? pal.Lit : hovered ? pal.Hi : ink, lit || hovered ? pal.LitStroke : width);
    }

    /// <summary>Linear blend from a to b (t outside 0..1 extrapolates, clamped per channel).</summary>
    private static SKColor Lerp(SKColor a, SKColor b, float t)
    {
        static byte Mix(byte x, byte y, float t) => (byte)Math.Clamp(MathF.Round(x + (y - x) * t), 0, 255);
        return new(Mix(a.Red, b.Red, t), Mix(a.Green, b.Green, t), Mix(a.Blue, b.Blue, t), Mix(a.Alpha, b.Alpha, t));
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
        _shadow.Dispose();
        _flowDash?.Dispose();
        ClearPictures();
        _alertDash.Dispose();
    }
}
