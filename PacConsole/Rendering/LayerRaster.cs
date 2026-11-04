using SkiaSharp;

namespace PacConsole.Rendering;

/// <summary>Rasterizing recorded pictures into images of their device pixels, for <see cref="StillRunCache"/> and <see cref="PartImageCache"/>.</summary>
internal static class LayerRaster
{
    /// <summary>An image of exactly its device pixels is drawn 1:1, so no filtering.</summary>
    public static readonly SKSamplingOptions Nearest = new(SKFilterMode.Nearest);

    /// <summary>A picture as an image of exactly its device pixels: its cull rect snapped outward to the pixel grid.</summary>
    public static (SKImage? Image, SKRect Dest) Rasterize(SKPicture picture, float scale)
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

    /// <summary>
    /// A worker's fault: frees what it made and logs it, where a fire-and-forget task would drop it unobserved.
    /// Nothing waits for these images, so the plant keeps drawing those runs and parts as vectors.
    /// </summary>
    public static void Failed(SKImage?[] images, Exception ex)
    {
        foreach (var image in images) image?.Dispose();
        typeof(SolidRenderer).Log().LogError(ex, "Rasterizing the plant's cached layers failed; they stay vector drawings");
    }
}
