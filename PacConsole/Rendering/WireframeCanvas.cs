using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Rendering;

/// <summary>
/// The main 3D viewport surface. PlantViewport owns the camera and calls <see cref="Show"/>
/// with a finished frame description; this element only draws it. SKCanvasElement draws
/// straight onto the window's Skia canvas, so there is no buffer copy per frame.
/// </summary>
public sealed class WireframeCanvas : SKCanvasElement
{
    private readonly WireframeRenderer _renderer = new();
    private CameraPose _pose = CameraPose.Iso;
    private UnitId? _selected;
    private IReadOnlyCollection<UnitId> _alerting = [];
    private IReadOnlyList<TagPlacement> _tags = [];

    public WireframeCanvas()
    {
        Unloaded += (_, _) => _renderer.Dispose();
    }

    /// <summary>Snapshots the frame on the UI thread, then asks for a redraw.</summary>
    public void Show(CameraPose pose, UnitId? selected, IReadOnlyCollection<UnitId> alerting, IReadOnlyList<TagPlacement> tags)
    {
        _pose = pose;
        _selected = selected;
        _alerting = alerting;
        _tags = tags;
        Invalidate();
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var projection = OrbitProjection.ForViewport(_pose, (float)area.Width, (float)area.Height);
        _renderer.DrawPlant(canvas, projection, PlantGeometry.Default, _selected, _alerting, _tags);
    }
}
