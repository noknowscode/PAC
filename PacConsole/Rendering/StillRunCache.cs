using PacConsole.Core.Plant;
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
/// worker rasterizes each run into an image of its device pixels, which the GPU keeps as a texture. The worker is never
/// waited for: WebAssembly has one thread and runs a worker only after the current call returns.
/// </summary>
internal sealed class StillRunCache : IDisposable
{
    // A recorded run is still vector work: each frame the render thread rasterizes its paths again
    // inside the damage region, and the fans and flow marks damage the plant's whole rect 20 times a
    // second (trace, 2026-10-16: skipping the runs cut idle CPU from ~68% to ~43% of a core). Once a
    // still frame has held for two frames, a worker rasterizes each run into an image of its device
    // pixels; an immutable raster image is uploaded to the GPU once and then drawn as a texture.
    // Picture-backed images (SKImage.FromPicture) were tried first: they re-rendered every frame.
    private const int StillFramesBeforeLayers = 2;

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

    /// <summary>Raised on a worker thread when cached layers are ready; the owner should redraw.</summary>
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
    /// runs. True when it started, so the owner can make the part images a coming selection will lift from
    /// (<see cref="PartImageCache"/>).
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
            if (image is not null) canvas.DrawImage(image, dest, LayerRaster.Nearest);
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
                    if (pictures[i] is { } picture) (images[i], dests[i]) = LayerRaster.Rasterize(picture, scale);
                }
            }
            catch (Exception ex)
            {
                LayerRaster.Failed(images, ex);
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

    public void Dispose()
    {
        lock (_layerGate)
        {
            _disposed = true;
            foreach (var image in _readyLayers?.Images ?? []) image?.Dispose();
            _readyLayers = null;
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
