using PacConsole.Core.Geometry;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>
/// Paints solids back to front, the way a technical isometric is inked: the stand first, then one flat ground shadow
/// for everything standing on it, then each part. Shadows fall away from a light at the camera's upper left, so they
/// read the same at every yaw; a lifted unit's shadow slides out from under it. This class decides how a frame is drawn
/// and hands the work to its parts: <see cref="SolidGeometry"/> (shapes, order, shadow and paths per camera),
/// <see cref="PartPainter"/> (one part's inks), <see cref="StillRunCache"/> (recorded runs and their cached layers),
/// <see cref="PartImageCache"/> (each part's image for lift frames) and <see cref="SystemMotion"/> (the isolated system's
/// live motion).
/// </summary>
public sealed class SolidRenderer : IDisposable
{
    private readonly SolidGeometry _geometry = new();
    private readonly StillRunCache _still = new();
    private readonly PartImageCache _partImages = new();
    private readonly PartPainter _painter;
    private readonly SystemMotion _motion;

    private int[]? _sequenceOrder;
    private readonly List<int> _sequence = [];

    public SolidRenderer(SolidPalette palette)
    {
        Palette = palette;
        _painter = new PartPainter(palette, _geometry);
        _motion = new SystemMotion(palette);
        _still.Ready += () => StillLayersReady?.Invoke();
        _partImages.Ready += () => StillLayersReady?.Invoke();
    }

    /// <summary>The theme's colours; a theme change makes a new renderer.</summary>
    public SolidPalette Palette { get; }

    /// <summary>Device pixels per drawing unit (the window's rasterization scale), so cached still layers match the screen 1:1.</summary>
    public float Scale { get; set; } = 1;

    /// <summary>Raised on a worker thread when cached still layers or part images are ready; the owner should redraw.</summary>
    public event Action? StillLayersReady;

    /// <summary>Paints <paramref name="parts"/> back to front. Lit parts of the selected unit take the accent; of an alerting unit, a dashed alert outline.</summary>
    public void Draw(SKCanvas canvas, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame)
    {
        var cameraChanged = _geometry.Update(parts, projection, frame);
        var lifts = _geometry.Lifts;

        // The stand (plate, pads, strip) lies under everything, so it goes first; then the shadows
        // that fall on it; then every other part in depth order. -1 stands for the shadow pass.
        if (!ReferenceEquals(_sequenceOrder, _geometry.Order))
        {
            _sequenceOrder = _geometry.Order;
            _sequence.Clear();
            foreach (var index in _geometry.Order) if (SolidGeometry.IsGround(parts[index])) _sequence.Add(index);
            _sequence.Add(-1);
            foreach (var index in _geometry.Order) if (!SolidGeometry.IsGround(parts[index])) _sequence.Add(index);
        }

        void Op(SKCanvas c, int index, PartPass pass)
        {
            if (index < 0) _painter.DrawShadow(c);
            else _painter.DrawPart(c, parts[index], index, projection, frame, SolidGeometry.LiftOf(frame, parts[index].Unit), pass);
        }

        // A moving camera or lift draws directly: recording would only add cost to a frame that is
        // thrown away. Once the view holds, static runs are recorded once and replayed, so a still
        // plant costs a few picture replays plus its live parts (trace: ~2,500 paths a frame otherwise).
        var key = new StillKey(_geometry.Camera, frame.Selected, frame.Hovered, SolidGeometry.AlertMask(frame.Alerting), frame.Details, frame.System, Palette, Scale);
        if (_still.Moved(cameraChanged, lifts))
        {
            _still.Clear();
            if (cameraChanged)
            {
                foreach (var index in _sequence) Op(canvas, index, PartPass.All);
            }
            else
            {
                // A lift at a still camera: each part is its cached image, raised by its lift; a part
                // whose look has no image yet (the unit just selected or let go) draws as vector until
                // a worker has one.
                _partImages.TakePartImages(parts.Count, projection, Palette, Scale);
                foreach (var index in _sequence)
                {
                    if (index >= 0 && _partImages.TryPartImage(index, _painter.LookOf(parts[index], index, frame), out var image, out var dest))
                    {
                        dest.Offset(0, projection.Rise(lifts[index]));
                        canvas.DrawImage(image, dest, LayerRaster.Nearest);
                        if (_geometry.Live[index]) Op(canvas, index, PartPass.Motion);
                    }
                    else
                    {
                        Op(canvas, index, PartPass.All);
                    }
                }
                StartPartImages(canvas.LocalClipBounds, parts, projection, frame);
            }
            _motion.Draw(canvas, parts, projection, frame, _geometry);
            return;
        }
        if (_still.NeedsRecording(key))
        {
            _still.Record(key, canvas.LocalClipBounds, _sequence, _geometry.Live, Op);
        }
        else if (_still.Hold(Scale))
        {
            // Ready ahead of the next selection, whose lift draws from these.
            _partImages.TakePartImages(parts.Count, projection, Palette, Scale);
            StartPartImages(canvas.LocalClipBounds, parts, projection, frame);
        }
        _still.Replay(canvas, (c, index) => Op(c, index, PartPass.Motion));
        _motion.Draw(canvas, parts, projection, frame, _geometry);
    }

    private void StartPartImages(SKRect bounds, IReadOnlyList<SolidPart> parts, OrbitProjection projection, SolidFrame frame) =>
        _partImages.StartPartImages(bounds, parts.Count, i => _painter.LookOf(parts[i], i, frame), _geometry.Live,
            (c, i, pass) => _painter.DrawPart(c, parts[i], i, projection, frame, 0, pass), Scale);

    public void Dispose()
    {
        _painter.Dispose();
        _motion.Dispose();
        _geometry.Dispose();
        _still.Dispose();
        _partImages.Dispose();
    }
}
