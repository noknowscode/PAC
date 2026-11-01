using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Rendering;

/// <summary>
/// The main 3D viewport surface: the plant as solids, plus the leader lines that tie each floating
/// tag to its unit. PlantViewport owns the camera and the tag layout and calls <see cref="Show"/>;
/// both use <see cref="PlantSolids.ViewProjection"/>, so tags and drawing share one projection.
/// </summary>
public sealed partial class PlantCanvas : SKCanvasElement
{
    private static readonly ImmutableArray<SolidPart> Parts = PlantSolids.Default.All;

    // Created on Loaded, disposed on Unloaded: an element can be unloaded and loaded again, and a
    // render after Dispose touches a freed native path. RenderOverride runs on the desktop host's render
    // thread, so a frame draws under _drawGate and the UI thread swaps the renderer, the leader paint and
    // the frame under it.
    private SolidRenderer? _renderer;
    private SKPaint? _leaderPaint;
    private readonly object _drawGate = new();
    private Frame _frame = new(CameraPose.Iso, null, ImmutableArray<UnitId>.Empty, ImmutableArray<TagPlacement>.Empty,
        ImmutableDictionary<UnitId, float>.Empty, 0, null, PlantSystem.All, true);

    /// <summary>
    /// Everything one frame draws. Built on the UI thread from immutable values and handed over whole, so the
    /// render thread never reads a field the UI thread is writing.
    /// </summary>
    private sealed record Frame(
        CameraPose Pose,
        UnitId? Selected,
        IReadOnlyCollection<UnitId> Alerting,
        IReadOnlyList<TagPlacement> Tags,
        IReadOnlyDictionary<UnitId, float> Lift,
        double Seconds,
        UnitId? Hovered,
        PlantSystem System,
        bool Motion);

    public PlantCanvas()
    {
        Loaded += (_, _) =>
        {
            if (_renderer is null) Swap(CreateRenderer());
            OnPaletteChanged(_renderer!.Palette);
            Invalidate();
        };
        Unloaded += (_, _) => Swap(null);
        ActualThemeChanged += (_, _) => OnThemeChanged();
    }

    /// <summary>Hands the next frame to the render thread, then asks for a redraw.</summary>
    public void Show(CameraPose pose, UnitId? selected, ImmutableArray<UnitId> alerting, ImmutableArray<TagPlacement> tags,
        ImmutableDictionary<UnitId, float> lift, double seconds, UnitId? hovered, PlantSystem system, bool motion)
    {
        var frame = new Frame(pose, selected, alerting, tags, lift, seconds, hovered, system, motion);
        lock (_drawGate) _frame = frame;
        Invalidate();
    }

    private void OnThemeChanged()
    {
        if (_renderer is null) return;
        var renderer = CreateRenderer();
        Swap(renderer);
        OnPaletteChanged(renderer.Palette);
        Invalidate();
    }

    /// <summary>
    /// Replaces the renderer once no frame is drawing with it, then frees the old one. With no renderer (Unloaded)
    /// the leader paint goes too; the next renderer brings a new one.
    /// </summary>
    private void Swap(SolidRenderer? next)
    {
        SolidRenderer? old;
        SKPaint? oldPaint = null;
        lock (_drawGate)
        {
            old = _renderer;
            _renderer = next;
            if (next is null) (oldPaint, _leaderPaint) = (_leaderPaint, null);
            else _leaderPaint ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };
        }
        old?.Dispose();
        oldPaint?.Dispose();
    }

    private SolidRenderer CreateRenderer()
    {
        var renderer = new SolidRenderer { Palette = SolidPalette.For(ActualTheme) };
        var queue = DispatcherQueue;
        // Cached still layers arrive on a worker; one more frame puts them on screen.
        renderer.StillLayersReady += () => queue.TryEnqueue(() => { if (ReferenceEquals(_renderer, renderer)) Invalidate(); });
        return renderer;
    }

    private void OnPaletteChanged(SolidPalette palette)
    {
        lock (_drawGate)
        {
            if (_leaderPaint is not null) _leaderPaint.Color = palette.Leader;
        }
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        lock (_drawGate)
        {
            if (_renderer is null || _leaderPaint is null) return;
            var f = _frame;
            _renderer.Scale = (float)(XamlRoot?.RasterizationScale ?? 1);
            var projection = PlantSolids.ViewProjection(f.Pose, (float)area.Width, (float)area.Height);
            _renderer.Draw(canvas, Parts, projection, new SolidFrame(f.Selected, f.Alerting, f.Lift, f.Seconds, Hovered: f.Hovered, System: f.System, Motion: f.Motion));

            // Leaders follow the tags, which move every animation frame. Drawn as a line and a box per tag
            // (opaque ink), so a frame builds no path (SkiaSharp 4 paths are immutable) and boxes no enumerator.
            var tags = f.Tags;
            for (var i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                canvas.DrawLine(t.Anchor.X, t.Anchor.Y, t.CenterX, t.Bottom, _leaderPaint);
                canvas.DrawRect(new SKRect(t.Anchor.X - 2, t.Anchor.Y - 2, t.Anchor.X + 2, t.Anchor.Y + 2), _leaderPaint);
            }
        }
    }
}
