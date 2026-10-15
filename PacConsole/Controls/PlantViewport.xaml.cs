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

    // While something moves, frames come from CompositionTarget.Rendering, in step with the display. A 16 ms
    // DispatcherTimer ticked on Windows' 15.6 ms timer grid, alternating 16 and 31 ms frames
    // (measured p90 31 ms), which read as judder. Time comes from a Stopwatch, not the wall clock.
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private bool _looping;
    private double _lastFrameSeconds;
    private readonly Dictionary<UnitId, Button> _tags = [];
    private readonly bool _animationsEnabled = AnimationsEnabled();

    private CameraPose _pose = CameraPose.Iso;
    private bool _auto;
    private bool _hover;

    // Plant motion: per-unit lift toward its target, and a clock for fans and pipe flow.
    private readonly Dictionary<UnitId, float> _lift = UnitCatalog.All.ToDictionary(u => u.Id, _ => 0f);

    // The unit under the pointer (outlined), and whether the current press began on a tag button.
    private UnitId? _hovered;
    private bool _pressOnTag;
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
        _ambient = OnFrame;

        // Opens still: the full-detail drawing is cached while the camera holds, and fans and flow
        // keep moving. Orbiting is opt-in (the toggle) or a drag.
        _auto = false;
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


        RotateLeft.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw - MathF.PI / 4 });
        RotateRight.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw + MathF.PI / 4 });
        Reset.Click += (_, _) => GoTo(CameraPose.Iso);
        AutoOrbit.Click += (_, _) => { _auto = AutoOrbit.IsChecked == true; _tweening = false; Refresh(); };

        Root.PointerEntered += (_, _) => { _hover = true; Refresh(); };
        // Fallback: a pointer that arrives by warp (no enter event on some X11 setups) still pauses the orbit.
        Root.PointerMoved += (_, _) => { if (!_hover) { _hover = true; Refresh(); } };
        Root.PointerExited += (_, _) => { _hover = false; _hovered = null; Refresh(); };
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
        Unloaded += (_, _) => { StopLoop(); AmbientClock.Unsubscribe(_ambient); };
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
        DependencyProperty.Register(nameof(SelectedUnit), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => { if (!SameValue(e.OldValue, e.NewValue)) ((PlantViewport)d).Refresh(); }));

    /// <summary>Units in alert (an <see cref="ImmutableArray{UnitId}"/>), drawn coral and dashed.</summary>
    public object? AlertingUnits
    {
        get => GetValue(AlertingUnitsProperty);
        set => SetValue(AlertingUnitsProperty, value);
    }

    public static readonly DependencyProperty AlertingUnitsProperty =
        DependencyProperty.Register(nameof(AlertingUnits), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => { if (!SameValue(e.OldValue, e.NewValue)) ((PlantViewport)d).Draw(); }));

    /// <summary>
    /// The view is recomposed every second, so these properties receive a new boxed value (a new
    /// alerting array) each time even when nothing changed. Redraw only for a real change.
    /// </summary>
    internal static bool SameValue(object? a, object? b) =>
        Equals(a, b) || (a is ImmutableArray<UnitId> x && b is ImmutableArray<UnitId> y && x.SequenceEqual(y));

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
        // Display-rate frames only while the camera or a lift moves. Fans and pipe flow keep turning
        // on the shared 20 fps ambient clock (the same tick as the status-dot pulse), not on a held
        // Rendering subscription, which would force frames at display rate forever.
        var moving = IsOrbiting || _tweening || !LiftSettled;
        if (moving) StartLoop(); else StopLoop();
        if (!moving && _animationsEnabled && IsLoaded)
        {
            if (!_ambientOn) _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
            _ambientOn = true;
            AmbientClock.Subscribe(_ambient);
        }
        else
        {
            _ambientOn = false;
            AmbientClock.Unsubscribe(_ambient);
        }

        AutoOrbitLabel.Text = !_auto ? "Auto orbit" : _hover || _dragging ? "Orbit paused" : "Orbiting";
        Draw();
    }

    private void StartLoop()
    {
        if (_looping) return;
        _looping = true;
        _lastFrameSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopLoop()
    {
        if (!_looping) return;
        _looping = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, object e) => OnFrame();

    private readonly Action _ambient;
    private bool _ambientOn;

    private void OnFrame()
    {
        var now = DateTimeOffset.Now;
        var nowSeconds = _clock.Elapsed.TotalSeconds;
        // Capped so a hitch (GC, a telemetry refresh) never makes the camera or a lift jump; the cap
        // sits above the ambient clock's 50 ms so fans keep their speed on it.
        var dt = (float)Math.Min(nowSeconds - _lastFrameSeconds, 0.1);
        _lastFrameSeconds = nowSeconds;

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
            var label = alerting.Contains(u.Id) ? $"{u.Code} · alert" : u.Code;
            return new TagRequest(u.Id, projection.Project(PlantSolids.Default.Anchors[u.Id] with { Y = PlantSolids.Default.Anchors[u.Id].Y + _lift[u.Id] }), TagLayout.EstimateWidth(label));
        }).ToList();
        var placements = TagLayout.Solve(requests, width);

        foreach (var p in placements)
        {
            var tag = _tags[p.Unit];
            var info = UnitCatalog.Get(p.Unit);
            var isAlert = alerting.Contains(p.Unit);
            var isSelected = selected == p.Unit;
            // Only touch what changed: a new Content string re-measures and re-shapes the label's
            // text, and this runs every animation frame.
            var label = isAlert ? $"{info.Code} · alert" : info.Code;
            if (!Equals(tag.Content, label)) tag.Content = label;
            if (tag.Width != p.Width) tag.Width = p.Width;
            Canvas.SetLeft(tag, p.Left);
            Canvas.SetTop(tag, p.Top);
            ApplyTagState(tag, isSelected, isAlert);
        }

        PlantDrawing.Show(_pose, selected, alerting, placements, _lift, _seconds, _hovered);
        Pose = _pose;

        var caption = $"{PresetName()}, yaw {_pose.YawDegrees:000}°, elevation {_pose.ElevationDegrees:00}°";
        if (CaptionDetail.Text != caption) CaptionDetail.Text = caption;
        var alertNames = string.Join(", ", alerting.Select(u => UnitCatalog.Get(u).Code));
        var help = $"6 units. {(alerting.IsEmpty ? "No alerts" : $"{alertNames} in alert")}. " +
            $"{(selected is { } s ? UnitCatalog.Get(s).Code + " selected" : "No unit selected")}.";
        if (AutomationProperties.GetHelpText(this) != help) AutomationProperties.SetHelpText(this, help);
    }

    private string PresetName() =>
        CameraPresets.All.FirstOrDefault(p => _pose.IsNear(p.Pose))?.Label ?? "Free orbit";

    private static void ApplyTagState(Button tag, bool selected, bool alert)
    {
        var key = selected ? "Selected" : alert ? "Alert" : "Normal";
        if (Equals(tag.DataContext, key)) return; // avoid restyling every frame
        tag.DataContext = key;
        var r = Application.Current.Resources;
        // A plate in the ground colour (so the label reads over line work), no outline. The selected
        // plate is the one orange object on the page; an alert keeps its word in the label.
        tag.Background = (Brush)r[selected ? "AccentBrush" : "OverlayBrush"];
        tag.Foreground = (Brush)r[selected ? "OnAccentBrush" : alert ? "AlertTextBrush" : "TextPrimaryBrush"];
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Don't capture yet: a press that doesn't move must still click a tag.
        _pressAt = e.GetCurrentPoint(Surface).Position;
        _pressPose = _pose;
        _pressOnTag = IsInTagLayer(e.OriginalSource as DependencyObject);
    }

    private bool IsInTagLayer(DependencyObject? d)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, TagLayer)) return true;
        }
        return false;
    }

    /// <summary>The unit drawn under a point on the surface, using the same projection as the drawing.</summary>
    private UnitId? UnitAt(Windows.Foundation.Point at)
    {
        var projection = PlantSolids.ViewProjection(_pose, (float)Surface.ActualWidth, (float)Surface.ActualHeight);
        return PlantSolids.Default.UnitAt(projection, new((float)at.X, (float)at.Y), _lift);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Surface).Position;
        if (_pressAt is not { } start)
        {
            // Hover: outline the unit under the pointer, so it is clear the drawing is clickable.
            var hovered = IsInTagLayer(e.OriginalSource as DependencyObject) ? null : UnitAt(p);
            if (hovered != _hovered)
            {
                _hovered = hovered;
                Draw();
            }
            return;
        }
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
        if (_dragging)
        {
            Surface.ReleasePointerCapture(e.Pointer);
        }
        else if (!_pressOnTag && _pressAt is not null && UnitAt(e.GetCurrentPoint(Surface).Position) is { } unit)
        {
            // A click (no drag) on a unit's drawing selects it, as its tag does.
            SelectCommand?.Execute(unit);
        }
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
