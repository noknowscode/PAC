using Microsoft.UI.Xaml.Automation;
using System.Windows.Input;
using Microsoft.UI.Xaml.Input;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Rendering;
using Windows.System;

namespace PacConsole.Controls;

/// <summary>
/// The orbiting 3D plant plus its floating unit tags and toolbar.
/// Owns the camera state machine (Orbiting, Paused, Idle, Tweening, Dragging) because the
/// camera changes every frame and is view state, not domain data. The render loop runs only
/// while something moves; Idle and Paused redraw only when a bound property changes.
/// </summary>
public sealed partial class PlantViewport : UserControl
{
    private const float OrbitRadPerSecond = 0.12f;
    private const float DragYawPerPx = 0.008f;
    private const float DragElevationPerPx = 0.005f;
    private const double DragThresholdPx = 4;
    private static readonly TimeSpan TweenDuration = TimeSpan.FromMilliseconds(600);

    // The selected unit lifts off its pad: exponential follow, so a new selection mid-lift just
    // retargets (the figure's tau 0.28 s). Reduced motion snaps.
    private const double LiftTau = 0.28;
    private static readonly TimeSpan FrameFast = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan FrameAmbient = TimeSpan.FromMilliseconds(33);

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Dictionary<UnitId, Button> _tags = [];
    private readonly bool _animationsEnabled = AnimationsEnabled();

    private CameraPose _pose = CameraPose.Iso;
    private DateTimeOffset _lastFrame;
    private bool _auto;
    private bool _hover;

    // Plant motion: per-unit lift toward its target, and a clock for fans and pipe flow.
    private readonly Dictionary<UnitId, float> _lift = UnitCatalog.All.ToDictionary(u => u.Id, _ => 0f);
    private double _seconds;

    // Tween
    private CameraPose _tweenFrom, _tweenTo;
    private DateTimeOffset _tweenStart;
    private bool _tweening;

    // Drag
    private Windows.Foundation.Point? _pressAt;
    private CameraPose _pressPose;
    private bool _dragging;

    public PlantViewport()
    {
        InitializeComponent();

        _auto = _animationsEnabled;
        AutoOrbit.IsChecked = _auto;

        foreach (var unit in UnitCatalog.All)
        {
            var tag = new Button
            {
                Style = (Style)Application.Current.Resources["TagButton"],
                Tag = unit.Id,
            };
            AutomationProperties.SetName(tag, $"Select {unit.Code} {unit.Name}");
            tag.Click += (_, _) => SelectCommand?.Execute(unit.Id);
            _tags[unit.Id] = tag;
            TagLayer.Children.Add(tag);
        }

        _timer.Tick += (_, _) => OnFrame();
        RotateLeft.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw - MathF.PI / 4 });
        RotateRight.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw + MathF.PI / 4 });
        Reset.Click += (_, _) => GoTo(CameraPose.Iso);
        AutoOrbit.Click += (_, _) => { _auto = AutoOrbit.IsChecked == true; _tweening = false; Refresh(); };

        Root.PointerEntered += (_, _) => { _hover = true; Refresh(); };
        // Fallback: a pointer that arrives by warp (no enter event on some X11 setups) still pauses the orbit.
        Root.PointerMoved += (_, _) => { if (!_hover) { _hover = true; Refresh(); } };
        Root.PointerExited += (_, _) => { _hover = false; Refresh(); };
        GotFocus += (_, _) => { _hover = true; Refresh(); };
        LostFocus += (_, _) => { _hover = false; Refresh(); };

        // handledEventsToo: a child (the canvas element) marks presses handled on Skia hosts,
        // which would otherwise hide them from the drag logic.
        Surface.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        Surface.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        Surface.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        Surface.PointerCaptureLost += (_, _) => EndDrag();
        KeyDown += OnKeyDown;

        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>The current camera pose, updated every frame (a <see cref="CameraPose"/>).</summary>
    public object Pose
    {
        get => GetValue(PoseProperty);
        private set => SetValue(PoseProperty, value);
    }

    public static readonly DependencyProperty PoseProperty =
        DependencyProperty.Register(nameof(Pose), typeof(object), typeof(PlantViewport), new PropertyMetadata(CameraPose.Iso));

    /// <summary>The selected unit (a <see cref="UnitId"/>), drawn in the accent.</summary>
    public object? SelectedUnit
    {
        get => GetValue(SelectedUnitProperty);
        set => SetValue(SelectedUnitProperty, value);
    }

    public static readonly DependencyProperty SelectedUnitProperty =
        DependencyProperty.Register(nameof(SelectedUnit), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, _) => ((PlantViewport)d).Refresh()));

    /// <summary>Units in alert (an <see cref="ImmutableArray{UnitId}"/>), drawn coral and dashed.</summary>
    public object? AlertingUnits
    {
        get => GetValue(AlertingUnitsProperty);
        set => SetValue(AlertingUnitsProperty, value);
    }

    public static readonly DependencyProperty AlertingUnitsProperty =
        DependencyProperty.Register(nameof(AlertingUnits), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, _) => ((PlantViewport)d).Draw()));

    /// <summary>Executed with a <see cref="UnitId"/> when a tag is clicked.</summary>
    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(PlantViewport), new PropertyMetadata(null));

    /// <summary>Eases the camera to <paramref name="pose"/> and switches auto-orbit off.</summary>
    public void GoTo(CameraPose pose)
    {
        _auto = false;
        AutoOrbit.IsChecked = false;
        TweenTo(pose);
    }

    private CameraPose TargetOrCurrent() => _tweening ? _tweenTo : _pose;

    private void TweenTo(CameraPose target)
    {
        _auto = false;
        AutoOrbit.IsChecked = false;
        target = target.Clamped();
        if (!_animationsEnabled)
        {
            _pose = target;
            _tweening = false;
            Refresh();
            return;
        }
        _tweenFrom = _pose;
        // Take the short way round, but keep accumulated turns so yaw never jumps.
        _tweenTo = target with { Yaw = _pose.Yaw + CameraPose.Wrap(target.Yaw - _pose.Yaw) };
        _tweenStart = DateTimeOffset.Now;
        _tweening = true;
        Refresh();
    }

    private bool IsOrbiting => _auto && !_hover && !_dragging;

    private float LiftTarget(UnitId id) => SelectedUnit as UnitId? == id ? SolidRenderer.FullLift : 0;

    private bool LiftSettled => _lift.All(kv => kv.Value == LiftTarget(kv.Key));

    /// <summary>Starts or stops the frame loop to match the state, then draws once.</summary>
    private void Refresh()
    {
        if (!_animationsEnabled)
        {
            foreach (var id in _lift.Keys.ToList()) _lift[id] = LiftTarget(id);
        }
        // The camera or a lift moving needs full rate; fans and pipe flow alone run at 30 fps.
        var moving = IsOrbiting || _tweening || !LiftSettled;
        _timer.Interval = moving ? FrameFast : FrameAmbient;
        var needsLoop = moving || _animationsEnabled;
        if (needsLoop && !_timer.IsEnabled)
        {
            _lastFrame = DateTimeOffset.Now;
            _timer.Start();
        }
        else if (!needsLoop && _timer.IsEnabled)
        {
            _timer.Stop();
        }

        AutoOrbitLabel.Text = !_auto ? "AUTO ORBIT OFF" : _hover || _dragging ? "AUTO ORBIT PAUSED" : "AUTO ORBIT ON";
        Draw();
    }

    private void OnFrame()
    {
        var now = DateTimeOffset.Now;
        var dt = (float)(now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        _seconds += dt;
        var follow = (float)(1 - Math.Exp(-dt / LiftTau));
        foreach (var id in _lift.Keys.ToList())
        {
            var target = LiftTarget(id);
            var next = _lift[id] + (target - _lift[id]) * follow;
            _lift[id] = Math.Abs(target - next) < 0.02f ? target : next;
        }

        if (_tweening)
        {
            var t = Math.Clamp((now - _tweenStart) / TweenDuration, 0, 1);
            var e = 1 - Math.Pow(1 - t, 3); // cubic ease-out
            _pose = new CameraPose(
                (float)(_tweenFrom.Yaw + (_tweenTo.Yaw - _tweenFrom.Yaw) * e),
                (float)(_tweenFrom.Elevation + (_tweenTo.Elevation - _tweenFrom.Elevation) * e));
            if (t >= 1) _tweening = false;
        }
        else if (IsOrbiting)
        {
            _pose = _pose with { Yaw = _pose.Yaw + OrbitRadPerSecond * dt };
        }

        Refresh();
    }

    private void Draw()
    {
        var width = (float)Surface.ActualWidth;
        var height = (float)Surface.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var selected = SelectedUnit as UnitId?;
        var alerting = AlertingUnits is ImmutableArray<UnitId> a ? a : ImmutableArray<UnitId>.Empty;
        // One projection for the drawing and the tags, so leaders land on their units at any pose.
        var projection = PlantSolids.ViewProjection(_pose, width, height);

        var requests = UnitCatalog.All.Select(u =>
        {
            var label = alerting.Contains(u.Id) ? $"{u.Code} · ALERT" : u.Code;
            return new TagRequest(u.Id, projection.Project(PlantSolids.Default.Anchors[u.Id] with { Y = PlantSolids.Default.Anchors[u.Id].Y + _lift[u.Id] }), TagLayout.EstimateWidth(label));
        }).ToList();
        var placements = TagLayout.Solve(requests, width);

        foreach (var p in placements)
        {
            var tag = _tags[p.Unit];
            var info = UnitCatalog.Get(p.Unit);
            var isAlert = alerting.Contains(p.Unit);
            var isSelected = selected == p.Unit;
            tag.Content = isAlert ? $"{info.Code} · ALERT" : info.Code;
            tag.Width = p.Width;
            Canvas.SetLeft(tag, p.Left);
            Canvas.SetTop(tag, p.Top);
            ApplyTagState(tag, isSelected, isAlert);
        }

        PlantDrawing.Show(_pose, selected, alerting, placements, _lift, _seconds);
        Pose = _pose;

        CaptionDetail.Text = $"{PresetName()} · YAW {_pose.YawDegrees:000}° · EL {_pose.ElevationDegrees:00}°";
        var alertNames = string.Join(", ", alerting.Select(u => UnitCatalog.Get(u).Code));
        AutomationProperties.SetHelpText(this,
            $"6 units. {(alerting.IsEmpty ? "No alerts" : $"{alertNames} in alert")}. " +
            $"{(selected is { } s ? UnitCatalog.Get(s).Code + " selected" : "No unit selected")}.");
    }

    private string PresetName() =>
        CameraPresets.All.FirstOrDefault(p => _pose.IsNear(p.Pose))?.Label ?? "FREE ORBIT";

    private static void ApplyTagState(Button tag, bool selected, bool alert)
    {
        var key = selected ? "Selected" : alert ? "Alert" : "Normal";
        if (Equals(tag.DataContext, key)) return; // avoid restyling every frame
        tag.DataContext = key;
        var r = Application.Current.Resources;
        tag.Background = (Brush)r[selected ? "AccentBrush" : "OverlayBrush"];
        tag.BorderBrush = (Brush)r[selected ? "AccentBrush" : alert ? "AlertBrush" : "BorderBrushStrong"];
        tag.Foreground = (Brush)r[selected ? "OnAccentBrush" : alert ? "AlertTextBrush" : "TextPrimaryBrush"];
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Don't capture yet: a press that doesn't move must still click a tag.
        _pressAt = e.GetCurrentPoint(Surface).Position;
        _pressPose = _pose;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_pressAt is not { } start) return;
        var p = e.GetCurrentPoint(Surface).Position;
        double dx = p.X - start.X, dy = p.Y - start.Y;

        if (!_dragging)
        {
            if (Math.Sqrt(dx * dx + dy * dy) < DragThresholdPx) return;
            Surface.CapturePointer(e.Pointer);
            _dragging = true;
            _auto = false;
            AutoOrbit.IsChecked = false;
            _tweening = false;
        }

        _pose = new CameraPose(_pressPose.Yaw + (float)dx * DragYawPerPx, _pressPose.Elevation + (float)dy * DragElevationPerPx).Clamped();
        Refresh();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging) Surface.ReleasePointerCapture(e.Pointer);
        EndDrag();
    }

    private void EndDrag()
    {
        _pressAt = null;
        if (!_dragging) return;
        _dragging = false;
        Refresh();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var target = TargetOrCurrent();
        switch (e.Key)
        {
            case VirtualKey.Left: TweenTo(target with { Yaw = target.Yaw - MathF.PI / 4 }); break;
            case VirtualKey.Right: TweenTo(target with { Yaw = target.Yaw + MathF.PI / 4 }); break;
            case VirtualKey.Up: TweenTo(target with { Elevation = target.Elevation + MathF.PI / 18 }); break;
            case VirtualKey.Down: TweenTo(target with { Elevation = target.Elevation - MathF.PI / 18 }); break;
            case VirtualKey.Home: GoTo(CameraPose.Iso); break;
            default: return;
        }
        e.Handled = true;
    }

    private static bool AnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            // Not every host implements UISettings; default to motion on.
            return true;
        }
    }
}
