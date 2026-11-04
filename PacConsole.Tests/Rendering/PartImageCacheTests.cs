using PacConsole.Core.Geometry;
using PacConsole.Core.Solids;
using PacConsole.Rendering;

namespace PacConsole.Tests.Rendering;

public class PartImageCacheTests
{
    [Test]
    public void A_part_with_no_image_yet_hands_out_none()
    {
        using var cache = new PartImageCache();
        cache.TakePartImages(3, PlantFraming.ViewProjection(CameraPose.Iso, 560, 360), SolidRendererTests.Light, 1);

        cache.TryPartImage(0, default, out var image, out var dest).Should().BeFalse("nothing was rasterized yet");
        image.Should().BeNull();
        dest.Should().Be(default(SkiaSharp.SKRect));
    }
}
