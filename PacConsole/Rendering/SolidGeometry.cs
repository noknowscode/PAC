using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>
/// The plant's screen geometry at one camera, kept until the camera moves: each part's shape and still detail lines,
/// their paths, the depth order at the current lifts, the ground shadow and which parts belong to a system. Shapes
/// depend only on the camera and a lift is a screen translation, so a still camera with a lifting unit, turning fans
/// or moving flow rebuilds nothing, and re-sorts only when a lift moves. Paths are immutable (SkiaSharp 4): each is
/// built once through one reused <see cref="SKPathBuilder"/> and kept for the camera's life.
/// </summary>
internal sealed class SolidGeometry : IDisposable
{
    // Every path is built here and detached (the builder is empty again after each Detach), so no draw allocates a
    // builder. Winding, the builder's default, is the fill rule throughout.
    private readonly SKPathBuilder _pb = new();

    private IReadOnlyList<SolidPart>? _cachedParts;
    private float[] _liftBuffer = [];

    // A lift frame re-sorts only the pairs that involve the moving unit (trace, 2026-10-16: the full
    // pairwise sort was ~8 ms of every lift frame on a selection change).
    private readonly DepthCache _depthCache = new();

    /// <summary>The camera the shapes were built at.</summary>
    public (float, float, float, float, float, float) Camera { get; private set; }

    /// <summary>Each part's shape on screen; replaced (a new array) whenever the camera moves.</summary>
    public SolidShape[] Shapes { get; private set; } = [];

    /// <summary>Each still part's detail lines at this camera; null for a part without details or with animated ones.</summary>
    public DetailLine[]?[] Details { get; private set; } = [];

    /// <summary>Parts that change with time (fan blades, moving flow marks), drawn every frame.</summary>
    public bool[] Live { get; private set; } = [];

    /// <summary>Indices back to front at <see cref="Lifts"/>.</summary>
    public int[] Order { get; private set; } = [];

    /// <summary>Each part's lift the order was sorted at; replaced (a new array) whenever a lift moves.</summary>
    public float[] Lifts { get; private set; } = [];

    /// <summary>Every part's ground shadow as one path, or null before the first frame.</summary>
    public SKPath? Shadow { get; private set; }

    /// <summary>
    /// Brings the geometry to <paramref name="projection"/> and the frame's lifts. True when the camera (or the parts
    /// list) changed, which rebuilt every shape.
    /// </summary>
    public bool Update(IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame)
    {
        var camera = (projection.SinYaw, projection.CosYaw, projection.SinElevation, projection.Scale, projection.CenterX, projection.CenterY);
        var cameraChanged = !SameParts(parts, _cachedParts) || camera != Camera;
        if (cameraChanged)
        {
            _cachedParts = parts;
            Camera = camera;
            Shapes = new SolidShape[parts.Count];
            Details = new DetailLine[]?[parts.Count];
            ClearShapePaths();
            _shapePaths = new ShapePaths?[parts.Count];
            _detailPaths = new SKPath?[]?[parts.Count];
            Live = new bool[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                Shapes[i] = parts[i].Build(projection);
                if (parts[i].Details is { } d && !parts[i].AnimatedDetails) Details[i] = [.. d(projection, 0)];
                Live[i] = parts[i].AnimatedDetails || (Details[i]?.Any(l => l.Kind == DetailKind.Flow) ?? false);
            }
        }

        // Lifts are read into a reused buffer and copied only when one changed, so a frame that moves nothing (fans
        // and flow, 20 a second) allocates no array, and the arrays kept here name the lifts they were built at.
        if (_liftBuffer.Length != parts.Count) _liftBuffer = new float[parts.Count];
        for (var i = 0; i < parts.Count; i++) _liftBuffer[i] = LiftOf(frame, parts[i].Unit);
        if (cameraChanged || !_liftBuffer.AsSpan().SequenceEqual(Lifts))
        {
            var raised = _liftBuffer.AsSpan().ToArray();
            var volumes = new List<Volume>(parts.Count);
            for (var i = 0; i < parts.Count; i++) volumes.Add(parts[i].Volume.Raised(raised[i]));
            Order = DepthOrder.Sort(volumes, projection, _depthCache);
            Lifts = raised;
        }
        if (cameraChanged || Lifts.AsSpan().SequenceEqual(_shadowLift) is false) BuildShadows(parts, Lifts, projection, cameraChanged);
        return cameraChanged;
    }

    /// <summary>A unit's lift in <paramref name="frame"/>; 0 for parts that belong to no unit.</summary>
    public static float LiftOf(SolidFrame frame, UnitId? unit) =>
        unit is { } u && frame.Lift is not null && frame.Lift.TryGetValue(u, out var l) ? l : 0;

    /// <summary>
    /// The same parts list as last frame. Callers pass <c>ImmutableArray</c>s, a struct boxed afresh
    /// on every call, so reference equality alone always failed and every frame rebuilt and re-sorted
    /// every shape as if the camera had moved (Release trace, 2026-10-15: still plant at 12 fps).
    /// </summary>
    public static bool SameParts(IReadOnlyList<SolidPart> a, IReadOnlyList<SolidPart>? b) =>
        ReferenceEquals(a, b) || (a is ImmutableArray<SolidPart> x && b is ImmutableArray<SolidPart> y && x == y);

    /// <summary>Flat on the plate (plate, feet, pads, strip, label): no higher than a pad. Piers and sleepers stand up and cast shadows.</summary>
    public static bool IsGround(SolidPart part) =>
        part.Name.StartsWith("stand.", StringComparison.Ordinal) && part.Volume.Y1 <= PlantSolids.PadHeight + 0.01f;

    public static bool IsStand(SolidPart part) => part.Name.StartsWith("stand.", StringComparison.Ordinal);

    /// <summary>The units in alert as bits, for keys that must change when they do.</summary>
    public static int AlertMask(IReadOnlyCollection<UnitId> alerting)
    {
        var mask = 0;
        foreach (var u in alerting) mask |= 1 << (int)u;
        return mask;
    }

    // ---------------------------------------------------------------- shadows

    private float[] _shadowLift = [];

    // Each part's shadow on screen, kept per camera: a lift frame recomputes only the moving unit's.
    private Vec2[]?[] _shadowPolygons = [];
    private float[] _shadowPolygonLift = [];

    /// <summary>Shadow length per unit of height (a sun about 60 degrees up).</summary>
    private const float ShadowLength = 0.55f;

    /// <summary>
    /// One path holding every part's ground shadow: the hull of its base and its top, each pushed
    /// away from the light in proportion to its height above the surface it stands on (a unit's pad,
    /// or the plate). Drawn opaque in a single pass, so overlaps never darken.
    /// </summary>
    private void BuildShadows(IReadOnlyList<SolidPart> parts, float[] lifts, OrbitProjection p, bool cameraChanged)
    {
        _shadowLift = lifts;
        if (cameraChanged || _shadowPolygons.Length != parts.Count)
        {
            _shadowPolygons = new Vec2[]?[parts.Count];
            _shadowPolygonLift = new float[parts.Count];
            Array.Fill(_shadowPolygonLift, float.NaN);
        }
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
            if (_shadowPolygonLift[i] != lifts[i])
            {
                _shadowPolygonLift[i] = lifts[i];
                _shadowPolygons[i] = null;
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
                var screen = new Vec2[hull.Length];
                for (var k = 0; k < hull.Length; k++) screen[k] = p.Project(new(hull[k].X, ground, hull[k].Y));
                _shadowPolygons[i] = screen;
            }
            if (_shadowPolygons[i] is { } polygon) AddPolygon(polygon);
        }
        // A picture recorded with the old path keeps its own copy, so it can go now.
        Shadow?.Dispose();
        Shadow = _pb.Detach();
    }

    private static (float X, float Z) Unit(float x, float z)
    {
        var length = MathF.Sqrt(x * x + z * z);
        return length < 1e-5f ? (0, 0) : (x / length, z / length);
    }

    // ---------------------------------------------------------------- system membership

    private bool[] _membership = [];
    private PlantSystem _membershipSystem;
    private int _membershipAlerts;
    private IReadOnlyList<SolidPart>? _membershipParts;

    /// <summary>
    /// Whether part <paramref name="index"/> is in <paramref name="system"/>, worked out once per parts
    /// list and system (and, for Active alarms, per set of units in alarm).
    /// </summary>
    public bool InSystem(int index, PlantSystem system, IReadOnlyCollection<UnitId> alerting)
    {
        var alerts = system == PlantSystem.Alarms ? AlertMask(alerting) : 0;
        if (!ReferenceEquals(_membershipParts, _cachedParts) || _membershipSystem != system || _membershipAlerts != alerts)
        {
            var parts = _cachedParts!;
            _membership = new bool[parts.Count];
            for (var i = 0; i < parts.Count; i++) _membership[i] = PlantSolids.InSystem(parts[i], system, alerting);
            _membershipParts = _cachedParts;
            _membershipSystem = system;
            _membershipAlerts = alerts;
        }
        return _membership[index];
    }

    // ---------------------------------------------------------------- paths

    private ShapePaths?[] _shapePaths = [];
    private SKPath?[]?[] _detailPaths = [];

    /// <summary>
    /// One part's shape as paths at the current camera. Built on the part's first draw, then reused by every later
    /// draw at that camera (the still recording, each hover or selection re-recording, part images and lift frames)
    /// until the camera moves.
    /// </summary>
    public ShapePaths PathsOf(int index, bool crease)
    {
        if (_shapePaths[index] is { } built) return built;
        var shape = Shapes[index];
        var shades = new SKPath?[SolidShape.Shades];
        for (var s = 0; s < shades.Length; s++) shades[s] = Polygons(shape.ShadeFaces[s]);
        return _shapePaths[index] = new ShapePaths
        {
            Fill = Polygons(shape.Fill),
            Shades = shades,
            Top = Polygons(shape.Top),
            Outline = Lines(shape.Outline),
            Crease = crease ? Lines(shape.Crease) : null,
            Bevel = crease ? Lines(shape.Bevel) : null,
        };
    }

    /// <summary>The cache for a still part's detail lines: one slot per line, filled on first draw.</summary>
    public SKPath?[] DetailPathsOf(int index, int lines) => _detailPaths[index] ??= new SKPath?[lines];

    /// <summary>A line as a path of its own, for a part whose lines move and are built per draw.</summary>
    public SKPath LinePath(Vec2[] points)
    {
        AddLine(points);
        return _pb.Detach();
    }

    private void ClearShapePaths()
    {
        foreach (var paths in _shapePaths) paths?.Dispose();
        foreach (var lines in _detailPaths) foreach (var path in lines ?? []) path?.Dispose();
        _shapePaths = [];
        _detailPaths = [];
    }

    private SKPath? Polygons(Vec2[] polygon)
    {
        if (polygon.Length < 3) return null;
        AddPolygon(polygon);
        return _pb.Detach();
    }

    private SKPath? Polygons(List<Vec2[]> polygons)
    {
        if (polygons.Count == 0) return null;
        foreach (var polygon in polygons) AddPolygon(polygon);
        return _pb.Detach();
    }

    private SKPath? Lines(List<Vec2[]> lines)
    {
        if (lines.Count == 0) return null;
        foreach (var line in lines) AddLine(line);
        return _pb.Detach();
    }

    private void AddLine(Vec2[] line)
    {
        if (line.Length < 2) return;
        _pb.MoveTo(line[0].X, line[0].Y);
        for (var i = 1; i < line.Length; i++) _pb.LineTo(line[i].X, line[i].Y);
    }

    private void AddPolygon(Vec2[] polygon)
    {
        _pb.MoveTo(polygon[0].X, polygon[0].Y);
        for (var i = 1; i < polygon.Length; i++) _pb.LineTo(polygon[i].X, polygon[i].Y);
        _pb.Close();
    }

    public void Dispose()
    {
        _pb.Dispose();
        Shadow?.Dispose();
        ClearShapePaths();
    }
}

/// <summary>One part's shape as paths at the current camera. Null stands for nothing to draw.</summary>
internal sealed class ShapePaths : IDisposable
{
    public required SKPath? Fill { get; init; }
    public required SKPath?[] Shades { get; init; }
    public required SKPath? Top { get; init; }
    public required SKPath? Outline { get; init; }
    public SKPath? Crease { get; init; }
    public SKPath? Bevel { get; init; }

    public void Dispose()
    {
        Fill?.Dispose();
        foreach (var shade in Shades) shade?.Dispose();
        Top?.Dispose();
        Outline?.Dispose();
        Crease?.Dispose();
        Bevel?.Dispose();
    }
}
