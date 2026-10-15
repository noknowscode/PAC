using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace PacConsole.Rendering;

/// <summary>
/// A small plant drawing in the hero's solid style: either the whole plant from a fixed camera
/// preset (the preset buttons) or the selected unit on its pad, turning with the viewport (the unit
/// preview). Redraws only when a bound property changes.
/// </summary>
public sealed partial class SolidThumb : SKCanvasElement
{
    // Created on Loaded, disposed on Unloaded (see PlantCanvas).
    private SolidRenderer? _renderer;

    public SolidThumb()
    {
        Loaded += (_, _) =>
        {
            _renderer ??= new() { Palette = SolidPalette.FromResources(Application.Current.Resources) };
            Invalidate();
        };
        Unloaded += (_, _) => { _renderer?.Dispose(); _renderer = null; };
    }

    /// <summary>Camera pose (a <see cref="CameraPose"/>).</summary>
    public object? Pose
    {
        get => GetValue(PoseProperty);
        set => SetValue(PoseProperty, value);
    }

    public static readonly DependencyProperty PoseProperty =
        DependencyProperty.Register(nameof(Pose), typeof(object), typeof(SolidThumb), new PropertyMetadata(null, Redraw));

    /// <summary>Key of a <see cref="CameraPresets"/> entry ("iso", "elevation", "plan"); sets <see cref="Pose"/>.</summary>
    public string? PresetKey
    {
        get => (string?)GetValue(PresetKeyProperty);
        set => SetValue(PresetKeyProperty, value);
    }

    public static readonly DependencyProperty PresetKeyProperty =
        DependencyProperty.Register(nameof(PresetKey), typeof(string), typeof(SolidThumb), new PropertyMetadata(null, (d, e) =>
        {
            var preset = CameraPresets.All.FirstOrDefault(p => p.Key == e.NewValue as string);
            if (preset is not null) ((SolidThumb)d).Pose = preset.Pose;
        }));

    /// <summary>Selected unit (a <see cref="UnitId"/>), lit in the accent.</summary>
    public object? Selected
    {
        get => GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    public static readonly DependencyProperty SelectedProperty =
        DependencyProperty.Register(nameof(Selected), typeof(object), typeof(SolidThumb), new PropertyMetadata(null, Redraw));

    /// <summary>Units in alert (an <see cref="ImmutableArray{UnitId}"/>), outlined in the alert colour.</summary>
    public object? AlertingUnits
    {
        get => GetValue(AlertingUnitsProperty);
        set => SetValue(AlertingUnitsProperty, value);
    }

    public static readonly DependencyProperty AlertingUnitsProperty =
        DependencyProperty.Register(nameof(AlertingUnits), typeof(object), typeof(SolidThumb), new PropertyMetadata(null, Redraw));

    /// <summary>When true, draw only <see cref="Selected"/> on its pad, turning with <see cref="Pose"/>.</summary>
    public bool SingleUnit
    {
        get => (bool)GetValue(SingleUnitProperty);
        set => SetValue(SingleUnitProperty, value);
    }

    public static readonly DependencyProperty SingleUnitProperty =
        DependencyProperty.Register(nameof(SingleUnit), typeof(bool), typeof(SolidThumb), new PropertyMetadata(false, Redraw));

    private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SolidThumb)d).Invalidate();

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_renderer is null) return;
        var plant = PlantSolids.Default;
        var pose = Pose as CameraPose? ?? CameraPose.Iso;
        var selected = Selected as UnitId?;
        var alerting = AlertingUnits is ImmutableArray<UnitId> a ? a : ImmutableArray<UnitId>.Empty;
        float w = (float)area.Width, h = (float)area.Height;
        if (w <= 0 || h <= 0) return;

        if (SingleUnit)
        {
            if (selected is not { } unit) return;
            // Follows the viewport's yaw, turned half a radian so the preview shows another face.
            var (centre, radius) = plant.Sphere(unit);
            var p = OrbitProjection.AroundSphere(new CameraPose(pose.Yaw + 0.5f, 0.45f), centre, radius, w, h, 2);
            _renderer.Draw(canvas, plant.UnitWithPad(unit), p, selected, alerting);
            return;
        }

        _renderer.Draw(canvas, plant.All, PlantSolids.Fit(pose, plant.All, w, h, 4), selected, alerting);
    }
}
