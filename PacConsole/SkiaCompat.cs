using SkiaSharp;

/// <summary>
/// The studio branch builds on stable Uno (Uno.Sdk 6.7), which ships SkiaSharp 3. SkiaSharp 3 has no
/// <c>SKPathBuilder</c>, so this mirrors the part of SkiaSharp 4's builder the app uses over a mutable
/// <see cref="SKPath"/>: <see cref="Detach"/> hands the path over and starts a new one, as SkiaSharp 4's does.
/// </summary>
internal sealed class SKPathBuilder : IDisposable
{
    private SKPath _path = new();

    public SKPathBuilder MoveTo(float x, float y) { _path.MoveTo(x, y); return this; }
    public SKPathBuilder MoveTo(SKPoint point) { _path.MoveTo(point); return this; }
    public SKPathBuilder LineTo(float x, float y) { _path.LineTo(x, y); return this; }
    public SKPathBuilder LineTo(SKPoint point) { _path.LineTo(point); return this; }
    public SKPathBuilder CubicTo(float x0, float y0, float x1, float y1, float x2, float y2) { _path.CubicTo(x0, y0, x1, y1, x2, y2); return this; }
    public SKPathBuilder CubicTo(SKPoint point0, SKPoint point1, SKPoint point2) { _path.CubicTo(point0, point1, point2); return this; }
    public SKPathBuilder Close() { _path.Close(); return this; }
    public SKPathBuilder Reset() { _path.Reset(); return this; }

    /// <summary>Returns the path built so far and starts an empty one; the caller owns the returned path.</summary>
    public SKPath Detach()
    {
        var built = _path;
        _path = new SKPath();
        return built;
    }

    /// <summary>Returns a copy of the path built so far; the builder keeps its path.</summary>
    public SKPath Snapshot() => new(_path);

    public void Dispose() => _path.Dispose();
}
