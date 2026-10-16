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

    /// <summary>Each process system's ink: its outline, glow and motion when the plant view isolates it.</summary>
    public IReadOnlyDictionary<PlantSystem, SKColor> Systems { get; init; } = new Dictionary<PlantSystem, SKColor>();

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
            Systems = new Dictionary<PlantSystem, SKColor>
            {
                [PlantSystem.SteamWater] = C("SystemSteamWaterBrush"),
                [PlantSystem.Cooling] = C("SystemCoolingBrush"),
                [PlantSystem.FuelFlue] = C("SystemFuelFlueBrush"),
                [PlantSystem.Electrical] = C("SystemElectricalBrush"),
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
    UnitId? Hovered = null,
    PlantSystem System = PlantSystem.All,
    bool Motion = true);

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

    // System highlight: a soft halo behind each part of the isolated system, then its outline in the
    // system's ink. The blur is one cached mask filter; nothing here is built per frame.
    private readonly SKMaskFilter _glowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
    private readonly SKPaint _glow = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 3, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKMaskFilter _puffBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f);
    private readonly SKPaint _puff = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _ring = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.6f };

    /// <summary>The isolated system's ink for the part being drawn, or null when it is not highlighted.</summary>
    private SKColor? _systemInk;

    public required SolidPalette Palette { get; init; }

    /// <summary>Device pixels per drawing unit (the window's rasterization scale), so cached still layers match the screen 1:1.</summary>
    public float Scale { get; set; } = 1;

    /// <summary>Raised on a worker thread when cached still layers are ready; the owner should redraw.</summary>
    public event Action? StillLayersReady;

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
        var key = new StillKey(camera, frame.Selected, frame.Hovered, AlertMask(frame.Alerting), frame.Details, frame.System, Palette, Scale);
        var moving = cameraChanged || !lifts.AsSpan().SequenceEqual(_stillLift);
        _stillLift = lifts;
        if (moving)
        {
            ClearPictures();
            foreach (var index in sequence) Op(canvas, index);
            DrawMotion(canvas, parts, projection, frame, LiftOf);
            return;
        }
        if (_segments.Count == 0 || key != _stillKey)
        {
            ClearPictures();
            _stillKey = key;
            _stillFrames = 0;
            var bounds = canvas.LocalClipBounds;
            SKPictureRecorder? recorder = null;
            SKCanvas? recording = null;
            void Flush()
            {
                if (recorder is null) return;
                _segments.Add((recorder.EndRecording(), null, default, -2));
                recorder.Dispose();
                recorder = null;
                recording = null;
            }
            foreach (var index in sequence)
            {
                if (index >= 0 && _live[index])
                {
                    Flush();
                    _segments.Add((null, null, default, index));
                    continue;
                }
                if (recorder is null)
                {
                    recorder = new SKPictureRecorder();
                    // An R-tree trims each run's cull rect to what it draws, so its cached layer stays that size.
                    recording = recorder.BeginRecording(bounds, useRTree: true);
                }
                Op(recording!, index);
            }
            Flush();
        }
        else
        {
            TakeStillLayers();
            if (++_stillFrames >= StillFramesBeforeLayers && _layeredGeneration != _generation) StartStillLayers();
        }
        foreach (var (picture, image, dest, index) in _segments)
        {
            if (image is not null) canvas.DrawImage(image, dest, Nearest);
            else if (picture is not null) canvas.DrawPicture(picture);
            else Op(canvas, index);
        }
        DrawMotion(canvas, parts, projection, frame, LiftOf);
    }

    /// <summary>Where a system's motion starts: a point on a part, and the unit it lifts with.</summary>
    private readonly record struct Emitter(Vec3 At, UnitId? Unit, int Index);

    private IReadOnlyList<SolidPart>? _emitterParts;
    private Emitter[] _smoke = [], _plumes = [], _vents = [], _bushings = [];

    // Electrical: each generator bushing pairs with a transformer bushing (west to east), joined by
    // an overhead conductor that only the electrical view draws.
    private (Emitter From, Emitter To)[] _conductors = [];
    private readonly SKPath _arc = new();
    private readonly SKPaint _wire = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.2f, StrokeCap = SKStrokeCap.Round };

    /// <summary>How high a conductor arches above the straight line between its bushings, in world units.</summary>
    private const float ConductorArch = 6;

    /// <summary>
    /// Contextual motion for the isolated system, drawn live over the plant (never recorded): smoke
    /// from the stacks (fuel and flue gas), vapour plumes over the fan stacks (cooling), wisps from the
    /// drum's safety valve (steam and water) and energy beads along overhead conductors and pulse rings at the bushings (electrical). Every value
    /// follows from the clock, so a still frame and a moving one agree.
    /// </summary>
    private void DrawMotion(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame, Func<UnitId?, float> liftOf)
    {
        if (!frame.Motion || !frame.Details || frame.System == PlantSystem.All) return;
        if (!Palette.Systems.TryGetValue(frame.System, out var ink)) return;
        if (!SameParts(parts, _emitterParts)) FindEmitters(parts);
        var t = frame.Seconds;
        var scale = projection.Scale;

        Vec2 At(Emitter e, float dx, float dy) =>
            projection.Project(new Vec3(e.At.X + dx, e.At.Y + dy + liftOf(e.Unit), e.At.Z));

        // A soft puff rises dy world units over its life while it widens and fades; puffs are staggered.
        void Puffs(Emitter[] emitters, int count, double life, float rise, float drift, float r0, float r1, SKColor color, byte alpha)
        {
            _puff.MaskFilter = _puffBlur;
            foreach (var e in emitters)
            {
                for (var k = 0; k < count; k++)
                {
                    var phase = (float)Frac(t / life + (double)k / count + e.Index * 0.37);
                    var rising = 1 - (1 - phase) * (1 - phase); // ease-out: fast off the lip, slowing as it cools
                    var p = At(e, drift * phase, rise * rising);
                    var fade = (1 - phase) * Math.Min(1, phase * 6); // in over the first sixth, then out
                    _puff.Color = color.WithAlpha((byte)(alpha * fade));
                    canvas.DrawCircle(p.X, p.Y, (r0 + (r1 - r0) * phase) * scale, _puff);
                }
            }
            _puff.MaskFilter = null;
        }

        switch (frame.System)
        {
            case PlantSystem.FuelFlue:
                Puffs(_smoke, 6, 3.2, 30, 6, 3, 9, Lerp(ink, Palette.Lo, 0.4f), 0x90);
                break;
            case PlantSystem.Cooling:
                Puffs(_plumes, 4, 2.6, 24, 3, 3.5f, 10, Lerp(ink, Palette.Paper, 0.3f), 0x58);
                break;
            case PlantSystem.SteamWater:
                Puffs(_vents, 5, 2.2, 18, 4, 1.5f, 6, Lerp(ink, Palette.Paper, 0.2f), 0xB0);
                break;
            case PlantSystem.Electrical:
                // Energy along the overhead conductors: two beads per conductor make the 1.1 s trip from
                // the generator to the transformer, fading in and out at the ends, each a blurred halo
                // round a bright core.
                Vec2 Along((Emitter From, Emitter To) c, float u)
                {
                    var (a, b) = (c.From.At, c.To.At);
                    var lift = liftOf(c.From.Unit) * (1 - u) + liftOf(c.To.Unit) * u;
                    var y = a.Y + (b.Y - a.Y) * u + 0.5f + ConductorArch * 4 * u * (1 - u) + lift;
                    return projection.Project(new Vec3(a.X + (b.X - a.X) * u, y, a.Z + (b.Z - a.Z) * u));
                }
                for (var i = 0; i < _conductors.Length; i++)
                {
                    var c = _conductors[i];
                    _arc.Rewind();
                    var start = Along(c, 0);
                    _arc.MoveTo(start.X, start.Y);
                    for (var j = 1; j <= 16; j++)
                    {
                        var q = Along(c, j / 16f);
                        _arc.LineTo(q.X, q.Y);
                    }
                    _wire.Color = ink.WithAlpha(0x99);
                    canvas.DrawPath(_arc, _wire);
                    for (var k = 0; k < 2; k++)
                    {
                        var u = (float)Frac(t / 1.1 + k / 2.0 + i * 0.15);
                        var bead = Along(c, u);
                        var strength = MathF.Sin(MathF.PI * u);
                        _puff.MaskFilter = _puffBlur;
                        _puff.Color = ink.WithAlpha((byte)(0xB0 * strength));
                        canvas.DrawCircle(bead.X, bead.Y, 2.4f * scale, _puff);
                        _puff.MaskFilter = null;
                        _puff.Color = ink.WithAlpha((byte)(0xFF * strength));
                        canvas.DrawCircle(bead.X, bead.Y, 0.7f * scale, _puff);
                    }
                }
                // A ring leaves each bushing every 1.4 s and widens from 2 to 12 units as it fades.
                foreach (var e in _bushings)
                {
                    var phase = (float)Frac(t / 1.4 + e.Index * 0.5);
                    var p = At(e, 0, 0.5f);
                    _ring.Color = ink.WithAlpha((byte)(0xFF * (1 - phase)));
                    canvas.DrawCircle(p.X, p.Y, (2 + 10 * phase) * scale, _ring);
                }
                break;
        }
    }

    private static double Frac(double x) => x - Math.Floor(x);

    private void FindEmitters(IReadOnlyList<SolidPart> parts)
    {
        _emitterParts = parts;
        Emitter[] Tops(Func<SolidPart, bool> match) =>
            [.. parts.Where(match).Select((p, i) => new Emitter(TopCentre(p.Volume), p.Unit, i))];
        _smoke = Tops(p => p.Name == "stk.lip");
        _plumes = Tops(p => p.Name == "clg.stack");
        _vents = Tops(p => p.Name == "boiler.safety");
        _bushings = Tops(p => p.Name is "gen.bushing.cap" or "xfmr.bushing.cap");
        var from = Tops(p => p.Name == "gen.bushing.cap").OrderBy(e => e.At.X).ToArray();
        var to = Tops(p => p.Name == "xfmr.bushing.cap").OrderBy(e => e.At.X).ToArray();
        _conductors = [.. from.Zip(to)];
    }

    private static Vec3 TopCentre(Volume v)
    {
        float x = 0, z = 0;
        foreach (var q in v.Top) { x += q.X; z += q.Z; }
        return new Vec3(x / v.Top.Length, v.Y1, z / v.Top.Length);
    }

    private bool[] _live = [];
    private float[] _stillLift = [];
    private StillKey _stillKey;
    private readonly List<(SKPicture? Picture, SKImage? Image, SKRect Dest, int Part)> _segments = [];

    // A recorded run is still vector work: each frame the render thread rasterizes its paths again
    // inside the damage region, and the fans and flow marks damage the plant's whole rect 20 times a
    // second (trace, 2026-10-16: skipping the runs cut idle CPU from ~68% to ~43% of a core). Once a
    // still frame has held for two frames, a worker rasterizes each run into an image of its device
    // pixels; an immutable raster image is uploaded to the GPU once and then drawn as a texture.
    // Picture-backed images (SKImage.FromPicture) were tried first: they re-rendered every frame.
    private const int StillFramesBeforeLayers = 2;
    private static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);
    private int _stillFrames;
    private int _generation;
    private int _layeredGeneration = -1;
    private Task? _layerTask;
    private readonly object _layerGate = new();
    private (int Generation, SKImage?[] Images, SKRect[] Dests)? _readyLayers;
    private readonly List<SKPicture> _retired = [];

    /// <summary>What a recorded still frame depends on besides the camera and lifts.</summary>
    private readonly record struct StillKey(
        (float, float, float, float, float, float) Camera,
        UnitId? Selected,
        UnitId? Hovered,
        int Alerts,
        bool Details,
        PlantSystem System,
        SolidPalette? Palette,
        float Scale);

    private static int AlertMask(IReadOnlyCollection<UnitId> alerting)
    {
        var mask = 0;
        foreach (var u in alerting) mask |= 1 << (int)u;
        return mask;
    }

    private void ClearPictures()
    {
        _generation++;
        var busy = _layerTask is { IsCompleted: false };
        foreach (var (picture, image, _, _) in _segments)
        {
            // A worker may still be reading this run; it is disposed once that worker is done.
            if (picture is not null)
            {
                if (busy) _retired.Add(picture);
                else picture.Dispose();
            }
            image?.Dispose();
        }
        _segments.Clear();
        if (!busy) DisposeRetired();
    }

    private void DisposeRetired()
    {
        foreach (var picture in _retired) picture.Dispose();
        _retired.Clear();
    }

    /// <summary>Rasterizes the current runs on a worker, each snapped outward to the device pixel grid.</summary>
    private void StartStillLayers()
    {
        if (_layerTask is { IsCompleted: false }) return;
        var generation = _layeredGeneration = _generation;
        var scale = Scale;
        var pictures = _segments.Select(s => s.Picture).ToArray();
        _layerTask = Task.Run(() =>
        {
            var images = new SKImage?[pictures.Length];
            var dests = new SKRect[pictures.Length];
            for (var i = 0; i < pictures.Length; i++)
            {
                if (pictures[i] is not { } picture || picture.CullRect.IsEmpty) continue;
                var cull = picture.CullRect;
                var left = MathF.Floor(cull.Left * scale);
                var top = MathF.Floor(cull.Top * scale);
                var right = MathF.Ceiling(cull.Right * scale);
                var bottom = MathF.Ceiling(cull.Bottom * scale);
                using var surface = SKSurface.Create(new SKImageInfo((int)(right - left), (int)(bottom - top), SKColorType.Rgba8888, SKAlphaType.Premul));
                if (surface is null) continue;
                surface.Canvas.Clear(SKColors.Transparent);
                surface.Canvas.Translate(-left, -top);
                surface.Canvas.Scale(scale);
                surface.Canvas.DrawPicture(picture);
                images[i] = surface.Snapshot();
                dests[i] = new SKRect(left / scale, top / scale, right / scale, bottom / scale);
            }
            lock (_layerGate)
            {
                foreach (var stale in _readyLayers?.Images ?? []) stale?.Dispose();
                _readyLayers = (generation, images, dests);
            }
            StillLayersReady?.Invoke();
        });
    }

    /// <summary>Swaps in layers a worker finished for the runs on screen; drops layers for runs since replaced.</summary>
    private void TakeStillLayers()
    {
        if (_layerTask is { IsCompleted: true }) DisposeRetired();
        (int Generation, SKImage?[] Images, SKRect[] Dests)? ready;
        lock (_layerGate)
        {
            ready = _readyLayers;
            _readyLayers = null;
        }
        if (ready is not { } layers) return;
        if (layers.Generation != _generation || layers.Images.Length != _segments.Count)
        {
            foreach (var image in layers.Images) image?.Dispose();
            return;
        }
        for (var i = 0; i < _segments.Count; i++)
        {
            if (layers.Images[i] is { } image) _segments[i] = _segments[i] with { Image = image, Dest = layers.Dests[i] };
        }
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
        var inSystem = InSystem(index, frame.System);
        // Outside the chosen system a part fades toward the paper; the selected unit always stays inked.
        _fade = part.Unit is { } s && s == frame.Selected || inSystem ? 0 : SystemFade;
        // Inside an isolated system (not the ground, the selection or an alert), a part takes the system's ink.
        _systemInk = frame.System != PlantSystem.All && inSystem && !lit && !alert && !IsStand(part)
            && Palette.Systems.TryGetValue(frame.System, out var ink) ? ink : null;

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
        _fade = 0;
        _systemInk = null;
    }

    private bool[] _membership = [];
    private PlantSystem _membershipSystem;
    private IReadOnlyList<SolidPart>? _membershipParts;

    /// <summary>Whether part <paramref name="index"/> is in <paramref name="system"/>, worked out once per parts list and system.</summary>
    private bool InSystem(int index, PlantSystem system)
    {
        if (!ReferenceEquals(_membershipParts, _cachedParts) || _membershipSystem != system)
        {
            var parts = _cachedParts!;
            _membership = new bool[parts.Count];
            for (var i = 0; i < parts.Count; i++) _membership[i] = PlantSolids.InSystem(parts[i], system);
            _membershipParts = _cachedParts;
            _membershipSystem = system;
        }
        return _membership[index];
    }

    private static bool IsStand(SolidPart part) => part.Name.StartsWith("stand.", StringComparison.Ordinal);

    /// <summary>How far a part outside the chosen system fades toward the paper (0 = none, 1 = gone).</summary>
    private const float SystemFade = 0.78f;

    private float _fade;

    /// <summary>A colour as the current part draws it: faded toward the paper when it is outside the system.</summary>
    private SKColor Ink(SKColor c) => _fade == 0 ? c : Lerp(c, Palette.Paper, _fade);

    private void DrawDetails(SKCanvas canvas, IEnumerable<DetailLine> details, double seconds)
    {
        foreach (var d in details)
        {
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
                    StrokeLine(canvas, d.Points, _systemInk ?? Palette.Leader, fast ? 1.4f : 1);
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
        _line.Color = Ink(color);
        _line.StrokeWidth = width;
        canvas.DrawPath(_path, _line);
    }

    private void DrawShape(SKCanvas canvas, SolidShape shape, EdgeTone edge, bool crease, bool lit, bool alert, bool hovered = false, Circuit circuit = Circuit.None)
    {
        var pal = Palette;
        if (_systemInk is { } halo)
        {
            _glow.Color = halo.WithAlpha(0x73);
            _glow.MaskFilter = _glowBlur;
            AddOutline(shape.Outline);
            canvas.DrawPath(_path, _glow);
            _glow.MaskFilter = null;
        }
        if (circuit != Circuit.None && pal.Circuits.TryGetValue(circuit, out var tint))
        {
            // A circuit's pipe: shades run from its deep tone to its base, the top a step lighter than
            // the base, and the outline in the circuit's own ink, so a run reads end to end.
            Fill(canvas, shape.Fill, tint.Base);
            for (var s = 0; s < SolidShape.Shades; s++) Fill(canvas, shape.ShadeFaces[s], Lerp(tint.Deep, tint.Base, s / 3f));
            Fill(canvas, shape.Top, Lerp(tint.Base, tint.Deep, -0.35f));
            Stroke(canvas, shape.Outline, _systemInk ?? Lerp(tint.Deep, pal.Silhouette, 0.5f), _systemInk is null ? pal.SilhouetteStroke : pal.LitStroke);
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
        if (_systemInk is { } sys && !hovered)
        {
            Stroke(canvas, shape.Outline, sys, pal.LitStroke);
            return;
        }
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
        _fill.Color = Ink(color);
        canvas.DrawPath(_path, _fill);
    }

    private void Fill(SKCanvas canvas, List<Vec2[]> polygons, SKColor color)
    {
        if (polygons.Count == 0) return;
        _path.Rewind();
        foreach (var polygon in polygons) AddPolygon(polygon);
        _fill.Color = Ink(color);
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
        _line.Color = Ink(color);
        _line.StrokeWidth = width ?? Palette.Hairline;
        canvas.DrawPath(_path, _line);
    }

    private void AddOutline(List<Vec2[]> lines)
    {
        _path.Rewind();
        foreach (var line in lines)
        {
            if (line.Length < 2) continue;
            _path.MoveTo(line[0].X, line[0].Y);
            for (var i = 1; i < line.Length; i++) _path.LineTo(line[i].X, line[i].Y);
        }
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
        _fastFlowDash?.Dispose();
        _glow.Dispose();
        _glowBlur.Dispose();
        _puff.Dispose();
        _puffBlur.Dispose();
        _ring.Dispose();
        _arc.Dispose();
        _wire.Dispose();
        _layerTask?.Wait();
        ClearPictures();
        DisposeRetired();
        lock (_layerGate)
        {
            foreach (var image in _readyLayers?.Images ?? []) image?.Dispose();
            _readyLayers = null;
        }
        _alertDash.Dispose();
    }
}
