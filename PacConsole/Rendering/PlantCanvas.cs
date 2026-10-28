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
    // thread, so a frame draws under _drawGate and the UI thread swaps the renderer under it (Swap).
    private SolidRenderer? _renderer;
    private readonly object _drawGate = new();
    private readonly SKPath _leaders = new();
    private readonly SKPaint _leaderPaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1 };

    private CameraPose _pose = CameraPose.Iso;
    private UnitId? _selected;
    private IReadOnlyCollection<UnitId> _alerting = [];
    private IReadOnlyList<TagPlacement> _tags = [];
    private IReadOnlyDictionary<UnitId, float>? _lift;
    private double _seconds;
    private UnitId? _hovered;
    private PlantSystem _system;
    private bool _motion = true;

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

    /// <summary>Snapshots the frame on the UI thread, then asks for a redraw.</summary>
    public void Show(CameraPose pose, UnitId? selected, IReadOnlyCollection<UnitId> alerting, IReadOnlyList<TagPlacement> tags, IReadOnlyDictionary<UnitId, float> lift, double seconds, UnitId? hovered, PlantSystem system, bool motion)
    {
        _system = system;
        _motion = motion;
        _pose = pose;
        _selected = selected;
        _alerting = alerting;
        _tags = tags;
        _lift = lift;
        _seconds = seconds;
        _hovered = hovered;
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

    /// <summary>Replaces the renderer once no frame is drawing with it, then frees the old one.</summary>
    private void Swap(SolidRenderer? next)
    {
        SolidRenderer? old;
        lock (_drawGate)
        {
            old = _renderer;
            _renderer = next;
        }
        old?.Dispose();
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
        lock (_drawGate) _leaderPaint.Color = palette.Leader;
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        lock (_drawGate)
        {
            if (_renderer is null) return;
            _renderer.Scale = (float)(XamlRoot?.RasterizationScale ?? 1);
            var projection = PlantSolids.ViewProjection(_pose, (float)area.Width, (float)area.Height);
            _renderer.Draw(canvas, Parts, projection, new SolidFrame(_selected, _alerting, _lift, _seconds, Hovered: _hovered, System: _system, Motion: _motion));

            _leaders.Rewind();
            foreach (var t in _tags)
            {
                _leaders.MoveTo(t.Anchor.X, t.Anchor.Y);
                _leaders.LineTo(t.CenterX, t.Bottom);
                _leaders.AddRect(new SKRect(t.Anchor.X - 2, t.Anchor.Y - 2, t.Anchor.X + 2, t.Anchor.Y + 2));
            }
            if (!_leaders.IsEmpty) canvas.DrawPath(_leaders, _leaderPaint);
        }
    }
}
