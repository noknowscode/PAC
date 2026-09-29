using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Rendering;

/// <summary>
/// A small wireframe: either the whole plant from a fixed camera preset (the preset buttons)
/// or one unit fitted by its bounding sphere (the unit preview). Redraws only when a bound
/// property changes.
/// </summary>
public sealed partial class WireframeThumb : SKCanvasElement
{
    private readonly WireframeRenderer _renderer = new();

    public WireframeThumb()
    {
        Unloaded += (_, _) => _renderer.Dispose();
    }

    /// <summary>Camera pose (a <see cref="CameraPose"/>).</summary>
    public object? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    public static readonly DependencyProperty PoseProperty =
        DependencyProperty.Register(nameof(Pose), typeof(object), typeof(WireframeThumb), new PropertyMetadata(null, Redraw));

    /// <summary>Key of a <see cref="CameraPresets"/> entry ("iso", "elevation", "plan"); sets <see cref="Pose"/>.</summary>
    public string? PresetKey
    {
        get => (string?)GetValue(PresetKeyProperty);
        set => SetValue(PresetKeyProperty, value);
    }

    public static readonly DependencyProperty PresetKeyProperty =
        DependencyProperty.Register(nameof(PresetKey), typeof(string), typeof(WireframeThumb), new PropertyMetadata(null, (d, e) =>
        {
            var preset = CameraPresets.All.FirstOrDefault(p => p.Key == e.NewValue as string);
            if (preset is not null) ((WireframeThumb)d).Pose = preset.Pose;
        }));

    /// <summary>Selected unit (a <see cref="UnitId"/>), drawn in the accent.</summary>
    public object? Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public static readonly DependencyProperty SelectedProperty =
        DependencyProperty.Register(nameof(Selected), typeof(object), typeof(WireframeThumb), new PropertyMetadata(null, Redraw));

    /// <summary>When true, draw only <see cref="Selected"/>, rotating with <see cref="Pose"/>.</summary>
    public bool SingleUnit
    {
        get => (bool)GetValue(SingleUnitProperty);
        set => SetValue(SingleUnitProperty, value);
    }

    public static readonly DependencyProperty SingleUnitProperty =
        DependencyProperty.Register(nameof(SingleUnit), typeof(bool), typeof(WireframeThumb), new PropertyMetadata(false, Redraw));

    private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WireframeThumb)d).Invalidate();

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var plant = PlantGeometry.Default;
        var pose = Pose as CameraPose? ?? CameraPose.Iso;
        var selected = Selected as UnitId?;
        float w = (float)area.Width, h = (float)area.Height;
        if (w <= 0 || h <= 0) return;

        if (SingleUnit)
        {
            if (selected is not { } unit) return;
            // Follows the viewport's yaw, turned half a radian so the preview shows another face.
            var p = OrbitProjection.AroundSphere(new CameraPose(pose.Yaw + 0.5f, 0.45f), plant.Centers[unit], plant.Radii[unit], w, h, 6);
            _renderer.DrawUnit(canvas, p, plant.Units[unit]);
            return;
        }

        var projection = OrbitProjection.Fit(pose, plant.AllSolid, w, h, 6);
        _renderer.DrawPlant(canvas, projection, plant, selected, [], strokeScale: 0.6f, glow: false);
    }
}
