using System.Diagnostics.CodeAnalysis;
using PacConsole.Core.Geometry;
using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>
/// Each part as an image at no lift, at the current camera, for lift frames at a still camera: a selection changes the
/// look of two units only, so only their parts are drawn again (trace, 2026-10-16: lift frames redrew all ~2,500 paths at
/// display rate, 11 s of render-thread time in 14 s with eight selections). A worker rasterizes the parts whose look has
/// no image yet; it is never waited for (WebAssembly has one thread and runs a worker only after the current call returns).
/// </summary>
internal sealed class PartImageCache : IDisposable
{
    private object? _partKey;
    private (SKImage Image, SKRect Dest)?[] _partImages = [];
    private PartLook?[] _partLooks = [];
    private PartLook?[] _partRequested = [];
    private Task? _partTask;
    private (object Key, int[] Indices, PartLook[] Looks, SKImage?[] Images, SKRect[] Dests)? _readyParts;
    private readonly object _gate = new();

    // Set once by Dispose. A worker that sees this stops rasterizing and disposes what it made instead of handing it over.
    private volatile bool _disposed;

    /// <summary>Raised on a worker thread when part images are ready; the owner should redraw.</summary>
    public event Action? Ready;

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
        lock (_gate)
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
    public bool TryPartImage(int index, PartLook look, [NotNullWhen(true)] out SKImage? image, out SKRect dest)
    {
        if (_partImages[index] is { } cached && _partLooks[index] == look)
        {
            (image, dest) = cached;
            return true;
        }
        (image, dest) = (null, default);
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
                for (var k = 0; k < pictures.Count && !_disposed; k++) (images[k], dests[k]) = LayerRaster.Rasterize(pictures[k], scale);
            }
            catch (Exception ex)
            {
                LayerRaster.Failed(images, ex);
                return;
            }
            finally
            {
                // This worker owns these recordings: each is disposed here, rasterized or not.
                foreach (var picture in pictures) picture.Dispose();
            }
            lock (_gate)
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

    public void Dispose()
    {
        ClearPartImages();
        lock (_gate)
        {
            _disposed = true;
            foreach (var image in _readyParts?.Images ?? []) image?.Dispose();
            _readyParts = null;
        }
    }
}
