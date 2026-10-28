using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Rendering;

/// <summary>
/// The whole plant from one standard camera, small: a thumbnail beside the main view. Drawn with the
/// main view's renderer, so selection, alerts and the isolated system read the same; still (no fans,
/// flow or fine detail at this size) and redrawn only when <see cref="Show"/> brings a change.
/// </summary>
public sealed partial class PlantThumb : SKCanvasElement
{
    private static readonly ImmutableArray<SolidPart> Parts = PlantSolids.Default.All;

    private SolidRenderer? _renderer;
    private CameraPose _pose = CameraPose.Iso;
    private UnitId? _selected;
    private ImmutableArray<UnitId> _alerting = [];
    private PlantSystem _system;

    public PlantThumb()
    {
        Loaded += (_, _) =>
        {
            _renderer ??= new() { Palette = SolidPalette.For(ActualTheme) };
            Invalidate();
        };
        Unloaded += (_, _) =>
        {
            _renderer?.Dispose();
            _renderer = null;
        };
        ActualThemeChanged += (_, _) => OnThemeChanged();
    }

    /// <summary>Key of a <see cref="CameraPresets"/> entry ("iso", "elevation", "plan").</summary>
    public string? PresetKey
    {
        get => (string?)GetValue(PresetKeyProperty);
        set => SetValue(PresetKeyProperty, value);
    }

    public static readonly DependencyProperty PresetKeyProperty =
        DependencyProperty.Register(nameof(PresetKey), typeof(string), typeof(PlantThumb), new PropertyMetadata(null, (d, e) =>
        {
            var thumb = (PlantThumb)d;
            if (CameraPresets.All.FirstOrDefault(p => p.Key == e.NewValue as string) is { } preset) thumb._pose = preset.Pose;
            thumb.Invalidate();
        }));

    /// <summary>Takes the main view's selection, alerts and system; redraws only when one changed.</summary>
    public void Show(UnitId? selected, ImmutableArray<UnitId> alerting, PlantSystem system)
    {
        if (selected == _selected && system == _system && alerting.SequenceEqual(_alerting)) return;
        _selected = selected;
        _alerting = alerting;
        _system = system;
        Invalidate();
    }

    private void OnThemeChanged()
    {
        if (_renderer is null) return;
        _renderer.Dispose();
        _renderer = new() { Palette = SolidPalette.For(ActualTheme) };
        Invalidate();
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_renderer is null || area.Width <= 0 || area.Height <= 0) return;
        _renderer.Scale = (float)(XamlRoot?.RasterizationScale ?? 1);
        // A thumbnail's camera never moves, so it fits the plant tightly at its own yaw (the main view's
        // framing holds every yaw, which leaves a fixed view half empty at this size).
        var projection = PlantSolids.Fit(_pose, Parts, (float)area.Width, (float)area.Height, 6);
        _renderer.Draw(canvas, Parts, projection, new SolidFrame(_selected, _alerting, Details: false, System: _system, Motion: false));
    }
}
