using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;
using PacConsole.Core.Solids;
using PacConsole.Rendering;
using Windows.System;

namespace PacConsole.Controls;

/// <summary>
/// The orbiting 3D plant plus its floating unit tags and toolbar. The camera is a <see cref="CameraRig"/> (Core, tested):
/// it changes every frame and is view state, not domain data. <see cref="FrameClock"/> runs frames only while something
/// moves, and nothing while nobody can see the plant; <see cref="TagOverlay"/> lays the tags out. This class wires them to
/// input and to the bindings: the view switcher, the thumbnails and the orbit toggle bind <see cref="RestingPreset"/> and
/// <see cref="IsAutoOrbit"/> two-way, and the camera tools bind its commands.
/// </summary>
public sealed partial class PlantViewport : UserControl
{
    private readonly CameraRig _rig = new(AnimationsEnabled());
    private readonly FrameClock _clock;
    private readonly TagOverlay _tags;
    private readonly PlantThumb[] _thumbs;

    // The unit under the pointer (outlined), and the current press: where, whether it began on a tag, and by touch.
    private UnitId? _hovered;
    private Windows.Foundation.Point? _pressAt;
    private bool _pressOnTag;
    private bool _pressIsTouch;

    // Set while the viewport writes its own state properties, so their callbacks do not read them as requests.
    private bool _syncing;

    public PlantViewport()
    {
        InitializeComponent();
        _clock = new FrameClock(this, OnFrame, Refresh);
        _tags = new TagOverlay(TagLayer, unit => SelectCommand?.Execute(unit), () => { _overlay = null; Draw(); });
        _thumbs = [ThumbIsoDrawing, ThumbElevationDrawing, ThumbPlanDrawing];

        RotateLeftCommand = new ViewCommand(() => Move(r => r.Rotate(-1, _clock.Now)));
        RotateRightCommand = new ViewCommand(() => Move(r => r.Rotate(1, _clock.Now)));
        ResetViewCommand = new ViewCommand(() => Move(r => r.GoTo(CameraPresets.All[0], _clock.Now)));

        Root.PointerEntered += (_, _) => Hold(true);
        // Fallback: a pointer that arrives by warp (no enter event on some X11 setups) still pauses the orbit.
        Root.PointerMoved += (_, _) => { if (!_rig.IsHeld) Hold(true); };
        Root.PointerExited += (_, _) =>
        {
            _rig.IsHeld = false;
            _hovered = null;
            Refresh();
        };
        GotFocus += (_, _) => Hold(true);
        LostFocus += (_, _) => Hold(false);

        // handledEventsToo: a child (the canvas element) marks presses handled on Skia hosts,
        // which would otherwise hide them from the drag logic.
        Surface.AddHandler(PointerPressedEvent, new PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        Surface.AddHandler(PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        Surface.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        Surface.PointerCaptureLost += (_, _) => EndDrag();
        // A touch pan the page's ScrollViewer takes over cancels the press: it was a scroll, not an orbit.
        Surface.PointerCanceled += (_, _) => EndDrag();
        KeyDown += OnKeyDown;

        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>The selected unit (a <see cref="UnitId"/>), drawn in the accent and lifted off its pad.</summary>
    public object? SelectedUnit
    {
        get => GetValue(SelectedUnitProperty);
        set => SetValue(SelectedUnitProperty, value);
    }

    public static readonly DependencyProperty SelectedUnitProperty =
        DependencyProperty.Register(nameof(SelectedUnit), typeof(object), typeof(PlantViewport), new PropertyMetadata(null, (d, e) =>
        {
            if (SameValue(e.OldValue, e.NewValue)) return;
            var view = (PlantViewport)d;
            view._rig.Selected = e.NewValue as UnitId?;
            view.Refresh();
        }));

    /// <summary>The panel title (the system in view, as the page names it).</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlantViewport), new PropertyMetadata("", (d, e) => ((PlantViewport)d).TitleText.Text = e.NewValue as string ?? ""));

    /// <summary>The auto-orbit toggle's label: "Auto orbit", or what the orbit is doing while it is on.</summary>
    public string OrbitStatus
    {
        get => (string)GetValue(OrbitStatusProperty);
        private set => SetValue(OrbitStatusProperty, value);
    }

    public static readonly DependencyProperty OrbitStatusProperty =
        DependencyProperty.Register(nameof(OrbitStatus), typeof(string), typeof(PlantViewport), new PropertyMetadata("Auto orbit"));

    /// <summary>Auto orbit on or off. The toggles bind it two-way; a drag, a tween or a view change turns it off here.</summary>
    public bool IsAutoOrbit
    {
        get => (bool)GetValue(IsAutoOrbitProperty);
        set => SetValue(IsAutoOrbitProperty, value);
    }

    public static readonly DependencyProperty IsAutoOrbitProperty =
        DependencyProperty.Register(nameof(IsAutoOrbit), typeof(bool), typeof(PlantViewport), new PropertyMetadata(false, (d, e) =>
        {
            var view = (PlantViewport)d;
            if (view._syncing || e.NewValue is not bool on || on == view._rig.IsAuto) return;
            view.Move(r => r.SetAuto(on));
        }));

    /// <summary>
    /// The key of the standard view the camera rests in ("iso", "elevation", "plan"), or null between them. The switcher
    /// segments and the thumbnails bind it two-way: one is checked while the camera rests in its view, and checking one
    /// moves the camera there.
    /// </summary>
    public string? RestingPreset
    {
        get => (string?)GetValue(RestingPresetProperty);
        set => SetValue(RestingPresetProperty, value);
    }

    public static readonly DependencyProperty RestingPresetProperty =
        DependencyProperty.Register(nameof(RestingPreset), typeof(string), typeof(PlantViewport), new PropertyMetadata(null, (d, e) =>
        {
            var view = (PlantViewport)d;
            if (view._syncing || e.NewValue is not string key || CameraPresets.All.FirstOrDefault(p => p.Key == key) is not { } preset) return;
            view.Move(r => r.GoTo(preset, view._clock.Now));
        }));

    /// <summary>Turns the camera 45° to the left (the camera tools).</summary>
    public ICommand RotateLeftCommand { get; }

    /// <summary>Turns the camera 45° to the right.</summary>
    public ICommand RotateRightCommand { get; }

    /// <summary>Back to the isometric view (the Home key does the same).</summary>
    public ICommand ResetViewCommand { get; }

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

    /// <summary>Executed with a <see cref="UnitId"/> when a tag or a unit's drawing is clicked.</summary>
    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public static readonly DependencyProperty SelectCommandProperty =
        DependencyProperty.Register(nameof(SelectCommand), typeof(ICommand), typeof(PlantViewport), new PropertyMetadata(null));

    /// <summary>Changes the camera, then refreshes the frame loop and the drawing.</summary>
    private void Move(Action<CameraRig> change)
    {
        change(_rig);
        Refresh();
    }

    private void Hold(bool held)
    {
        if (_rig.IsHeld == held) return;
        _rig.IsHeld = held;
        Refresh();
    }

    /// <summary>Starts or stops the frame loop to match the camera, mirrors its state to the bindings, then draws once.</summary>
    private void Refresh()
    {
        _rig.Settle();
        // Display-rate frames only while the camera or a lift moves. Fans and pipe flow keep turning on the shared
        // 20 fps ambient clock (the same tick as the status-dot pulse). Out of sight, neither runs; a tween or a lift
        // picks up where it is when the plant shows again.
        _clock.Run(_rig.IsMoving, _rig.AnimationsEnabled && IsLoaded);
        Sync(() =>
        {
            if (OrbitStatus != _rig.Status) OrbitStatus = _rig.Status;
            if (IsAutoOrbit != _rig.IsAuto) IsAutoOrbit = _rig.IsAuto;
        });
        Draw();
    }

    private void Sync(Action write)
    {
        _syncing = true;
        try
        {
            write();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnFrame(double dt)
    {
        _rig.Step(dt, _clock.Now);
        Refresh();
    }

    /// <summary>
    /// What the tags, the view switcher, the thumbnails and the help text depend on. Fans and flow change none of it,
    /// so an ambient frame (20 a second while the plant is shown) hands the drawing its clock and lays out nothing.
    /// </summary>
    private readonly record struct OverlayInputs(CameraPose Pose, ImmutableDictionary<UnitId, float> Lift, UnitId? Selected,
        ImmutableArray<UnitId> Alerting, PlantSystem System, float Width, float Height);

    private OverlayInputs? _overlay;

    private void Draw()
    {
        var width = (float)Surface.ActualWidth;
        var height = (float)Surface.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var selected = SelectedUnit as UnitId?;
        var system = ShownSystem is PlantSystem shown ? shown : PlantSystem.All;
        var alerting = AlertingUnits is ImmutableArray<UnitId> a ? a : ImmutableArray<UnitId>.Empty;
        var inputs = new OverlayInputs(_rig.Pose, _rig.Lift, selected, alerting, system, width, height);
        if (inputs != _overlay)
        {
            _overlay = inputs;
            UpdateOverlay(inputs);
        }
        PlantDrawing.Show(_rig.Pose, selected, alerting, _tags.Placements, _rig.Lift, _rig.Seconds, _hovered, system, _rig.AnimationsEnabled);
    }

    private void UpdateOverlay(OverlayInputs inputs)
    {
        _tags.Layout(inputs.Pose, inputs.Lift, inputs.Selected, inputs.Alerting, inputs.System, inputs.Width, inputs.Height);

        // The switcher and the thumbnails show the standard view the camera rests in, if any.
        var preset = _rig.RestingPreset;
        Sync(() => { if (RestingPreset != preset?.Key) RestingPreset = preset?.Key; });
        // Each thumbnail redraws only when the selection, alerts or system actually changed.
        foreach (var thumb in _thumbs) thumb.Show(inputs.Selected, inputs.Alerting, inputs.System);
        var alertNames = string.Join(", ", inputs.Alerting.Select(u => UnitCatalog.Get(u).Code));
        var help = $"{UnitCatalog.All.Length} units, {preset?.Label ?? "Free orbit"} view. {(inputs.Alerting.IsEmpty ? "No alerts" : $"{alertNames} in alert")}. " +
            $"{(inputs.Selected is { } s ? UnitCatalog.Get(s).Code + " selected" : "No unit selected")}.";
        if (AutomationProperties.GetHelpText(this) != help) AutomationProperties.SetHelpText(this, help);
    }

    // xaml-lint: allow codebehind - drag-to-orbit reads raw pointer deltas, which no command or gesture surface carries.
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Don't capture yet: a press that doesn't move must still click a tag.
        _pressAt = e.GetCurrentPoint(Surface).Position;
        _rig.Press();
        _pressOnTag = _tags.Contains(e.OriginalSource as DependencyObject);
        _pressIsTouch = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch;
    }

    /// <summary>The unit drawn under a point on the surface, using the same projection as the drawing.</summary>
    private UnitId? UnitAt(Windows.Foundation.Point at)
    {
        var projection = PlantSolids.ViewProjection(_rig.Pose, (float)Surface.ActualWidth, (float)Surface.ActualHeight);
        return PlantSolids.Default.UnitAt(projection, new((float)at.X, (float)at.Y), _rig.Lift);
    }

    // xaml-lint: allow codebehind - drag-to-orbit (see OnPointerPressed).
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(Surface).Position;
        if (_pressAt is not { } start)
        {
            // A finger has no hover; its moves after a press handed to the page scroll outline nothing.
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
            // Hover: outline the unit under the pointer, so it is clear the drawing is clickable.
            var hovered = _tags.Contains(e.OriginalSource as DependencyObject) ? null : UnitAt(p);
            if (hovered != _hovered)
            {
                _hovered = hovered;
                Draw();
            }
            return;
        }
        switch (_rig.DragBy(p.X - start.X, p.Y - start.Y, _pressIsTouch))
        {
            case DragStep.Pending:
                return;
            case DragStep.Scroll:
                // The panel sits in the page's vertical ScrollViewer: a mostly vertical finger drag scrolls the page.
                _pressAt = null;
                return;
            case DragStep.Started:
                Surface.CapturePointer(e.Pointer);
                break;
        }
        Refresh();
    }

    // xaml-lint: allow codebehind - drag-to-orbit (see OnPointerPressed).
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_rig.IsDragging)
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
        if (_rig.EndDrag()) Refresh();
    }

    // xaml-lint: allow codebehind - arrow keys orbit the camera, which is view state.
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var now = _clock.Now;
        switch (e.Key)
        {
            case VirtualKey.Left: Move(r => r.Rotate(-1, now)); break;
            case VirtualKey.Right: Move(r => r.Rotate(1, now)); break;
            case VirtualKey.Up: Move(r => r.Tilt(1, now)); break;
            case VirtualKey.Down: Move(r => r.Tilt(-1, now)); break;
            case VirtualKey.Home: Move(r => r.GoTo(CameraPresets.All[0], now)); break;
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
