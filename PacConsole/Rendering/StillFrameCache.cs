using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>What a recorded still frame depends on besides the camera and lifts.</summary>
internal readonly record struct StillKey(
    (float, float, float, float, float, float) Camera,
    UnitId? Selected,
    UnitId? Hovered,
    int Alerts,
    bool Details,
    PlantSystem System,
    SolidPalette? Palette,
    float Scale);

/// <summary>
/// What a still plant is drawn from instead of its ~2,500 paths. Once the view holds, the static runs between live parts
/// are recorded once and replayed (trace: a still plant otherwise drew every path each frame); after two still frames a
/// worker rasterizes each run into an image of its device pixels, which the GPU keeps as a texture. For a lift at a still
/// camera, each part is also kept as an image at no lift, so a selection redraws only the two units whose look changed.
/// Workers are never waited for: WebAssembly has one thread and runs a worker only after the current call returns.
/// </summary>
internal sealed class StillFrameCache : IDisposable
{
    // A recorded run is still vector work: each frame the render thread rasterizes its paths again
    // inside the damage region, and the fans and flow marks damage the plant's whole rect 20 times a
    // second (trace, 2026-10-16: skipping the runs cut idle CPU from ~68% to ~43% of a core). Once a
    // still frame has held for two frames, a worker rasterizes each run into an image of its device
    // pixels; an immutable raster image is uploaded to the GPU once and then drawn as a texture.
    // Picture-backed images (SKImage.FromPicture) were tried first: they re-rendered every frame.
    private const int StillFramesBeforeLayers = 2;
    public static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);

    private float[] _stillLift = [];
    private StillKey _stillKey;
    private readonly List<(SKPicture? Picture, SKImage? Image, SKRect Dest, int Part)> _segments = [];
    private int _stillFrames;
    private int _generation;
    private int _layeredGeneration = -1;
    private Task? _layerTask;
    private readonly object _layerGate = new();

    // Set once by Dispose. A worker that sees this stops rasterizing and disposes what it made instead of handing it over.
    private volatile bool _disposed;
    private (int Generation, SKImage?[] Images, SKRect[] Dests)? _readyLayers;
    private readonly List<SKPicture> _retired = [];

    /// <summary>Raised on a worker thread when cached layers or part images are ready; the owner should redraw.</summary>
    public event Action? Ready;

    /// <summary>True when the camera or a lift moved since the last frame, which drops the recorded runs.</summary>
    public bool Moved(bool cameraChanged, float[] lifts)
    {
        var moving = cameraChanged || !lifts.AsSpan().SequenceEqual(_stillLift);
        _stillLift = lifts;
        return moving;
    }

    /// <summary>True when the frame needs recording: nothing is recorded yet, or the selection, hover, alerts, system or palette changed.</summary>
    public bool NeedsRecording(StillKey key) => _segments.Count == 0 || key != _stillKey;

    /// <summary>
    /// Records the frame as runs: each run of still parts one picture; a live part's body closes a run, and only its
    /// moving marks are drawn each frame, in the same place in the order. <paramref name="draw"/> draws one entry of
    /// <paramref name="sequence"/> (-1 is the shadow pass) in the given pass.
    /// </summary>
    public void Record(StillKey key, SKRect bounds, IReadOnlyList<int> sequence, bool[] live, Action<SKCanvas, int, PartPass> draw)
    {
        Clear();
        _stillKey = key;
        _stillFrames = 0;
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
            if (recorder is null)
            {
                recorder = new SKPictureRecorder();
                // An R-tree trims each run's cull rect to what it draws, so its cached layer stays that size.
                recording = recorder.BeginRecording(bounds, useRTree: true);
            }
            if (index >= 0 && live[index])
            {
                draw(recording!, index, PartPass.Body);
                Flush();
                _segments.Add((null, null, default, index));
                continue;
            }
            draw(recording!, index, PartPass.All);
        }
        Flush();
    }

    /// <summary>
    /// A frame at the recorded key: swaps in layers a worker finished, and after two such frames starts rasterizing the
    /// runs. True when it started, so the owner can make the part images a coming selection will lift from.
    /// </summary>
    public bool Hold(float scale)
    {
        TakeStillLayers();
        if (++_stillFrames < StillFramesBeforeLayers || _layeredGeneration == _generation) return false;
        StartStillLayers(scale);
        return true;
    }

    /// <summary>Draws the runs, each as its image once a worker made one; <paramref name="motion"/> draws a live part's moving marks.</summary>
    public void Replay(SKCanvas canvas, Action<SKCanvas, int> motion)
    {
        foreach (var (picture, image, dest, index) in _segments)
        {
            if (image is not null) canvas.DrawImage(image, dest, Nearest);
            else if (picture is not null) canvas.DrawPicture(picture);
            else motion(canvas, index);
        }
    }

    /// <summary>Drops the recorded runs and their layers (a worker still reading one frees it when done).</summary>
    public void Clear()
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
    private void StartStillLayers(float scale)
    {
        if (_layerTask is { IsCompleted: false }) return;
        var generation = _layeredGeneration = _generation;
        var pictures = _segments.Select(s => s.Picture).ToArray();
        // Off the render thread where there are threads; on WebAssembly the same work runs queued on the one thread.
        _layerTask = Task.Run(() =>
        {
            var images = new SKImage?[pictures.Length];
            var dests = new SKRect[pictures.Length];
            try
            {
                for (var i = 0; i < pictures.Length && !_disposed; i++)
                {
                    if (pictures[i] is { } picture) (images[i], dests[i]) = Rasterize(picture, scale);
                }
            }
            catch (Exception ex)
            {
                Failed(images, ex);
                return;
            }
            lock (_layerGate)
            {
                if (_disposed)
                {
                    foreach (var image in images) image?.Dispose();
                    return;
                }
                foreach (var stale in _readyLayers?.Images ?? []) stale?.Dispose();
                _readyLayers = (generation, images, dests);
            }
            Ready?.Invoke();
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

    // ---------------------------------------------------------------- part images

    // One image per part at the current camera, for lift frames: a selection changes the look of two
    // units only, so only their parts are drawn again (trace, 2026-10-16: lift frames redrew all
    // ~2,500 paths at display rate, 11 s of render-thread time in 14 s with eight selections).
    private object? _partKey;
    private (SKImage Image, SKRect Dest)?[] _partImages = [];
    private PartLook?[] _partLooks = [];
    private PartLook?[] _partRequested = [];
    private Task? _partTask;
    private (object Key, int[] Indices, PartLook[] Looks, SKImage?[] Images, SKRect[] Dests)? _readyParts;

    /// <summary>Starts over when the camera, palette or scale changed; swaps in images a worker finished.</summary>
    public void TakePartImages(int count, OrbitProjection p, SolidPalette palette, float scale)
    {
        var key = (p.SinYaw, p.CosYaw, p.SinElevation, p.Scale, p.CenterX, p.CenterY, palette, scale, count);
        if (!key.Equals(_partKey))
        {
            ClearPartImages();
            _partKey = key;
            _partImages = new (SKImage, SKRect)?[count];
            _partLooks = new PartLook?[count];
            _partRequested = new PartLook?[count];
        }
        (object Key, int[] Indices, PartLook[] Looks, SKImage?[] Images, SKRect[] Dests)? ready;
        lock (_layerGate)
        {
            ready = _readyParts;
            _readyParts = null;
        }
        if (ready is not { } r) return;
        for (var k = 0; k < r.Indices.Length; k++)
        {
            if (r.Images[k] is not { } image) continue;
            var i = r.Indices[k];
            if (!r.Key.Equals(_partKey))
            {
                image.Dispose();
                continue;
            }
            _partImages[i]?.Image.Dispose();
            _partImages[i] = (image, r.Dests[k]);
            _partLooks[i] = r.Looks[k];
        }
    }

    /// <summary>Part <paramref name="index"/>'s image at no lift, when one exists for <paramref name="look"/>.</summary>
    public bool TryPartImage(int index, PartLook look, out SKImage image, out SKRect dest)
    {
        if (_partImages[index] is { } cached && _partLooks[index] == look)
        {
            (image, dest) = cached;
            return true;
        }
        (image, dest) = (null!, default);
        return false;
    }

    /// <summary>
    /// Records each part whose look has no image yet, at no lift, and rasterizes them on a worker. <paramref name="draw"/>
    /// draws one part unlifted in the given pass: a live part's body only, its moving marks being drawn each frame.
    /// </summary>
    public void StartPartImages(SKRect bounds, int count, Func<int, PartLook> lookOf, bool[] live, Action<SKCanvas, int, PartPass> draw, float scale)
    {
        if (_partTask is { IsCompleted: false } || _partKey is not { } key) return;
        var indices = new List<int>();
        var looks = new List<PartLook>();
        var pictures = new List<SKPicture>();
        for (var i = 0; i < count; i++)
        {
            var look = lookOf(i);
            if (_partLooks[i] == look || _partRequested[i] == look) continue;
            _partRequested[i] = look;
            using var recorder = new SKPictureRecorder();
            var recording = recorder.BeginRecording(bounds, useRTree: true);
            draw(recording, i, live[i] ? PartPass.Body : PartPass.All);
            indices.Add(i);
            looks.Add(look);
            pictures.Add(recorder.EndRecording());
        }
        if (indices.Count == 0) return;
        _partTask = Task.Run(() =>
        {
            var images = new SKImage?[pictures.Count];
            var dests = new SKRect[pictures.Count];
            try
            {
                for (var k = 0; k < pictures.Count && !_disposed; k++) (images[k], dests[k]) = Rasterize(pictures[k], scale);
            }
            catch (Exception ex)
            {
                Failed(images, ex);
                return;
            }
            finally
            {
                // This worker owns these recordings: each is disposed here, rasterized or not.
                foreach (var picture in pictures) picture.Dispose();
            }
            lock (_layerGate)
            {
                if (_disposed)
                {
                    foreach (var image in images) image?.Dispose();
                    return;
                }
                if (_readyParts is { } stale) foreach (var image in stale.Images) image?.Dispose();
                _readyParts = (key, [.. indices], [.. looks], images, dests);
            }
            Ready?.Invoke();
        });
    }

    private void ClearPartImages()
    {
        foreach (var cached in _partImages) cached?.Image.Dispose();
        _partImages = [];
        _partLooks = [];
        _partRequested = [];
    }

    /// <summary>
    /// A worker's fault: frees what it made and logs it, where a fire-and-forget task would drop it unobserved.
    /// Nothing waits for these images, so the plant keeps drawing those runs and parts as vectors.
    /// </summary>
    private static void Failed(SKImage?[] images, Exception ex)
    {
        foreach (var image in images) image?.Dispose();
        typeof(SolidRenderer).Log().LogError(ex, "Rasterizing the plant's cached layers failed; they stay vector drawings");
    }

    /// <summary>A picture as an image of exactly its device pixels: its cull rect snapped outward to the pixel grid.</summary>
    private static (SKImage? Image, SKRect Dest) Rasterize(SKPicture picture, float scale)
    {
        var cull = picture.CullRect;
        if (cull.IsEmpty) return (null, default);
        var left = MathF.Floor(cull.Left * scale);
        var top = MathF.Floor(cull.Top * scale);
        var right = MathF.Ceiling(cull.Right * scale);
        var bottom = MathF.Ceiling(cull.Bottom * scale);
        using var surface = SKSurface.Create(new SKImageInfo((int)(right - left), (int)(bottom - top), SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null) return (null, default);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Translate(-left, -top);
        surface.Canvas.Scale(scale);
        surface.Canvas.DrawPicture(picture);
        return (surface.Snapshot(), new SKRect(left / scale, top / scale, right / scale, bottom / scale));
    }

    public void Dispose()
    {
        ClearPartImages();
        lock (_layerGate)
        {
            _disposed = true;
            foreach (var image in _readyLayers?.Images ?? []) image?.Dispose();
            _readyLayers = null;
            foreach (var image in _readyParts?.Images ?? []) image?.Dispose();
            _readyParts = null;
        }
        // The still runs' recordings, and runs retired while a worker read them, go once the layer
        // worker is done with them: now if it is idle, else when it finishes. Never a blocking wait.
        var pictures = new List<SKPicture>(_retired);
        _retired.Clear();
        foreach (var (picture, image, _, _) in _segments)
        {
            if (picture is not null) pictures.Add(picture);
            image?.Dispose();
        }
        _segments.Clear();
        if (_layerTask is { IsCompleted: false } layers) layers.ContinueWith(_ => DisposeAll(pictures), TaskScheduler.Default);
        else DisposeAll(pictures);
    }

    private static void DisposeAll(List<SKPicture> pictures)
    {
        foreach (var picture in pictures) picture.Dispose();
    }
}
