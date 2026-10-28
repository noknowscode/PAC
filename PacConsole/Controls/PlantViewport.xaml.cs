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

    // The view switcher's segments and thumbnails, read on every drawn frame.
    private readonly RadioButton[] _views;
    private readonly PlantThumb[] _thumbs;

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

        _views = [ViewIso, ViewElevation, ViewPlan, ThumbIso, ThumbElevation, ThumbPlan];
        _thumbs = [ThumbIsoDrawing, ThumbElevationDrawing, ThumbPlanDrawing];

        RotateLeft.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw - MathF.PI / 4 });
        RotateRight.Click += (_, _) => TweenTo(_pose with { Yaw = TargetOrCurrent().Yaw + MathF.PI / 4 });
        Reset.Click += (_, _) => GoTo(CameraPose.Iso);
        foreach (var view in _views)
        {
            view.Click += (sender, _) =>
            {
                if (sender is RadioButton { Tag: string key } && CameraPresets.All.FirstOrDefault(p => p.Key == key) is { } preset) GoTo(preset.Pose);
            };
        }
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

    /// <summary>The selected unit (a <see cref="UnitId"/>), drawn in the accent.</summary>
    public object? SelectedUnit
    {
        get => GetValue(SelectedUnitProperty);
        set => SetValue(SelectedUnitProperty, value);
    }

    public static readonly DependencyProperty SelectedUnitProperty =
        DependencyProperty.Register(nameof(SelectedUnit), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => { if (!SameValue(e.OldValue, e.NewValue)) ((PlantViewport)d).Refresh(); }));

    /// <summary>The panel title (the system in view, as the page names it).</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlantViewport), new PropertyMetadata("", (d, e) => ((PlantViewport)d).TitleText.Text = e.NewValue as string ?? ""));

    /// <summary>What the page puts at the left of the panel's footer bar (the equipment picker).</summary>
    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    public static readonly DependencyProperty FooterContentProperty =
        DependencyProperty.Register(nameof(FooterContent), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => ((PlantViewport)d).FooterHost.Content = e.NewValue));

    /// <summary>What the page puts beside the panel title (the systems filter).</summary>
    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public static readonly DependencyProperty HeaderContentProperty =
        DependencyProperty.Register(nameof(HeaderContent), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => ((PlantViewport)d).HeaderHost.Content = e.NewValue));

    /// <summary>The process system in view (a <see cref="PlantSystem"/>); equipment outside it is drawn faded.</summary>
    public object? ShownSystem
    {
        get => GetValue(ShownSystemProperty);
        set => SetValue(ShownSystemProperty, value);
    }

    public static readonly DependencyProperty ShownSystemProperty =
        DependencyProperty.Register(nameof(ShownSystem), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) => { if (!SameValue(e.OldValue, e.NewValue)) ((PlantViewport)d).Draw(); }));

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
            foreach (var unit in UnitCatalog.All) _lift[unit.Id] = LiftTarget(unit.Id);
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

    // xaml-lint: allow codebehind - the frame loop of a Skia viewport; it has no XAML surface.
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
        foreach (var unit in UnitCatalog.All)
        {
            var id = unit.Id;
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
        var system = ShownSystem is PlantSystem shown ? shown : PlantSystem.All;
        var alerting = AlertingUnits is ImmutableArray<UnitId> a ? a : ImmutableArray<UnitId>.Empty;
        // One projection for the drawing and the tags, so leaders land on their units at any pose.
        var projection = PlantSolids.ViewProjection(_pose, width, height);

        var requests = UnitCatalog.All.Select(u =>
        {
            var label = TagLabel(u, selected == u.Id, alerting.Contains(u.Id));
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
            var label = TagLabel(info, isSelected, isAlert);
            if (!Equals(tag.Content, label)) tag.Content = label;
            if (tag.Width != p.Width) tag.Width = p.Width;
            Canvas.SetLeft(tag, p.Left);
            Canvas.SetTop(tag, p.Top);
            ApplyTagState(tag, isSelected, isAlert);
            // Tags of equipment outside the system recede with their drawing.
            var opacity = isSelected || PlantSystems.Contains(system, p.Unit, alerting) ? 1.0 : 0.4;
            if (tag.Opacity != opacity) tag.Opacity = opacity;
        }

        PlantDrawing.Show(_pose, selected, alerting, placements, _lift, _seconds, _hovered, system, _animationsEnabled);

        // The view switcher shows the standard view the camera rests in, if any.
        var preset = CameraPresets.All.FirstOrDefault(p => _pose.IsNear(p.Pose))?.Key;
        foreach (var view in _views)
        {
            var on = Equals(view.Tag, preset);
            if (view.IsChecked != on) view.IsChecked = on;
        }
        // Each thumbnail redraws only when the selection, alerts or system actually changed.
        foreach (var thumb in _thumbs) thumb.Show(selected, alerting, system);
        var alertNames = string.Join(", ", alerting.Select(u => UnitCatalog.Get(u).Code));
        var help = $"{UnitCatalog.All.Length} units, {PresetName()} view. {(alerting.IsEmpty ? "No alerts" : $"{alertNames} in alert")}. " +
            $"{(selected is { } s ? UnitCatalog.Get(s).Code + " selected" : "No unit selected")}.";
        if (AutomationProperties.GetHelpText(this) != help) AutomationProperties.SetHelpText(this, help);
    }

    private string PresetName() =>
        CameraPresets.All.FirstOrDefault(p => _pose.IsNear(p.Pose))?.Label ?? "Free orbit";

    /// <summary>The callout text: the code alone, the name with it when selected, and the word "Alert" when in alert.</summary>
    private static string TagLabel(UnitInfo unit, bool selected, bool alert) =>
        (selected, alert) switch
        {
            (true, true) => $"{unit.Code} · {unit.Name} · Alert",
            (true, false) => $"{unit.Code} · {unit.Name}",
            (false, true) => $"{unit.Code} · Alert",
            _ => unit.Code,
        };

    private static void ApplyTagState(Button tag, bool selected, bool alert)
    {
        var key = selected ? (alert ? "SelectedAlert" : "Selected") : alert ? "Alert" : "Normal";
        if (Equals(tag.DataContext, key)) return; // avoid restyling every frame
        tag.DataContext = key;
        var r = Application.Current.Resources;
        // A plate in the ground colour (so the label reads over line work). The selected plate takes the
        // slate-teal outline, an alerting one the orange outline, and its label carries the word.
        tag.Background = (Brush)r["OverlayBrush"];
        tag.BorderThickness = new Thickness(selected || alert ? 1 : 0);
        tag.BorderBrush = (Brush)r[alert ? "AlertBorderBrush" : selected ? "AccentBrush" : "OverlayBrush"];
        tag.Foreground = (Brush)r[alert ? "AlertTextBrush" : selected ? "AccentBrush" : "TextPrimaryBrush"];
    }

    // xaml-lint: allow codebehind - drag-to-orbit reads raw pointer deltas, which no command or gesture surface carries.
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

    // xaml-lint: allow codebehind - drag-to-orbit (see OnPointerPressed).
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

    // xaml-lint: allow codebehind - drag-to-orbit (see OnPointerPressed).
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

    // xaml-lint: allow codebehind - arrow keys orbit the camera, which is view state.
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
