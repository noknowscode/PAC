using PacConsole.Core.Plant;

namespace PacConsole.Core.Geometry;

/// <summary>What a drag step did: still under the threshold, handed to the page as a scroll, started, or moved.</summary>
public enum DragStep { Pending, Scroll, Started, Moved }

/// <summary>
/// The plant view's camera as a state machine (Orbiting, Paused, Idle, Tweening, Dragging), plus each unit's lift off
/// its pad and the motion clock for fans and flow. Pure and clocked by its caller, so the rules are unit-tested; the
/// viewport only feeds it input and frame times and draws what it holds.
/// </summary>
public sealed class CameraRig(bool animationsEnabled)
{
    /// <summary>Auto orbit's turn rate.</summary>
    public const float OrbitRadPerSecond = 0.12f;
    public const float DragYawPerPx = 0.008f;
    public const float DragElevationPerPx = 0.005f;

    /// <summary>A press that moves less than this is a click, not a drag.</summary>
    public const double DragThresholdPx = 4;

    /// <summary>One rotate step (a quarter of a half turn) and one tilt step.</summary>
    public const float RotateStep = MathF.PI / 4, TiltStep = MathF.PI / 18;

    /// <summary>Lift at which a unit is fully raised, in world units (the figure's LIFT).</summary>
    public const float FullLift = 10;

    public static readonly TimeSpan TweenDuration = TimeSpan.FromMilliseconds(600);

    // The selected unit lifts off its pad: exponential follow, so a new selection mid-lift just
    // retargets (the figure's tau 0.28 s). Reduced motion snaps.
    private const double LiftTau = 0.28;

    private CameraPose _tweenFrom, _tweenTo, _pressPose;
    private double _tweenStart;

    public bool AnimationsEnabled { get; } = animationsEnabled;

    public CameraPose Pose { get; private set; } = CameraPose.Iso;

    /// <summary>Auto orbit is on (it turns only while nothing holds it: <see cref="IsOrbiting"/>).</summary>
    public bool IsAuto { get; private set; }

    /// <summary>A pointer is over the plant, or it has focus: the orbit pauses so the operator can aim.</summary>
    public bool IsHeld { get; set; }

    public bool IsDragging { get; private set; }

    public bool IsTweening { get; private set; }

    /// <summary>The unit that lifts off its pad.</summary>
    public UnitId? Selected { get; set; }

    /// <summary>Each unit's lift. Replaced, never changed in place, so a frame can hand it to the render thread.</summary>
    public ImmutableDictionary<UnitId, float> Lift { get; private set; } = UnitCatalog.All.ToImmutableDictionary(u => u.Id, _ => 0f);

    /// <summary>Seconds of motion so far: the fans' and the flow marks' clock.</summary>
    public double Seconds { get; private set; }

    public bool IsOrbiting => IsAuto && !IsHeld && !IsDragging;

    /// <summary>True while a frame at display rate changes something: the orbit, a tween or a lift.</summary>
    public bool IsMoving => IsOrbiting || IsTweening || !LiftSettled;

    /// <summary>The auto-orbit toggle's words for what the orbit is doing.</summary>
    public string Status => !IsAuto ? "Auto orbit" : IsHeld || IsDragging ? "Orbit paused" : "Orbiting";

    /// <summary>The standard view the camera rests in, if any.</summary>
    public CameraPreset? RestingPreset
    {
        get
        {
            foreach (var preset in CameraPresets.All)
            {
                if (Pose.IsNear(preset.Pose)) return preset;
            }
            return null;
        }
    }

    public float LiftTarget(UnitId unit) => Selected == unit ? FullLift : 0;

    public bool LiftSettled
    {
        get
        {
            foreach (var unit in UnitCatalog.All)
            {
                if (Lift[unit.Id] != LiftTarget(unit.Id)) return false;
            }
            return true;
        }
    }

    /// <summary>The auto-orbit toggle: turns the orbit on or off and ends a tween, so the camera stays where it is.</summary>
    public void SetAuto(bool on)
    {
        IsAuto = on;
        IsTweening = false;
    }

    /// <summary>Eases the camera to <paramref name="target"/> (at once with animations off) and stops the orbit.</summary>
    public void TweenTo(CameraPose target, double now)
    {
        IsAuto = false;
        target = target.Clamped();
        if (!AnimationsEnabled)
        {
            Pose = target;
            IsTweening = false;
            return;
        }
        _tweenFrom = Pose;
        // Take the short way round, but keep accumulated turns so yaw never jumps.
        _tweenTo = target with { Yaw = Pose.Yaw + CameraPose.Wrap(target.Yaw - Pose.Yaw) };
        _tweenStart = now;
        IsTweening = true;
    }

    /// <summary>A standard view (a switcher segment, a thumbnail, Reset, Home).</summary>
    public void GoTo(CameraPreset preset, double now) => TweenTo(preset.Pose, now);

    /// <summary>Turns by <paramref name="steps"/> × 45° from where the camera is heading.</summary>
    public void Rotate(int steps, double now)
    {
        var target = IsTweening ? _tweenTo : Pose;
        TweenTo(target with { Yaw = target.Yaw + RotateStep * steps }, now);
    }

    /// <summary>Tilts by <paramref name="steps"/> × 10° from where the camera is heading (kept between plan and ground).</summary>
    public void Tilt(int steps, double now)
    {
        var target = IsTweening ? _tweenTo : Pose;
        TweenTo(target with { Elevation = target.Elevation + TiltStep * steps }, now);
    }

    /// <summary>A press on the plant: a drag from here orbits relative to this pose.</summary>
    public void Press() => _pressPose = Pose;

    /// <summary>
    /// The pointer is <paramref name="dx"/>, <paramref name="dy"/> from where it was pressed. A drag starts past the
    /// threshold and stops the orbit; a mostly vertical finger drag is the page's scroll, so a touch orbits on yaw only.
    /// </summary>
    public DragStep DragBy(double dx, double dy, bool touch)
    {
        var started = false;
        if (!IsDragging)
        {
            if (Math.Sqrt(dx * dx + dy * dy) < DragThresholdPx) return DragStep.Pending;
            if (touch && Math.Abs(dy) >= Math.Abs(dx)) return DragStep.Scroll;
            IsDragging = true;
            IsAuto = false;
            IsTweening = false;
            started = true;
        }
        Pose = new CameraPose(_pressPose.Yaw + (float)dx * DragYawPerPx, _pressPose.Elevation + (touch ? 0 : (float)dy * DragElevationPerPx)).Clamped();
        return started ? DragStep.Started : DragStep.Moved;
    }

    /// <summary>Ends a drag; true when one was in progress.</summary>
    public bool EndDrag()
    {
        var was = IsDragging;
        IsDragging = false;
        return was;
    }

    /// <summary>With animations off, every lift is at its target at once.</summary>
    public void Settle()
    {
        if (AnimationsEnabled) return;
        foreach (var unit in UnitCatalog.All) Lift = Lift.SetItem(unit.Id, LiftTarget(unit.Id));
    }

    /// <summary>
    /// One frame <paramref name="dt"/> seconds after the last, at <paramref name="now"/> on the caller's clock: lifts
    /// follow their targets, a tween eases (cubic out) toward its end, and the orbit turns.
    /// </summary>
    public void Step(double dt, double now)
    {
        Seconds += dt;
        var follow = (float)(1 - Math.Exp(-dt / LiftTau));
        foreach (var unit in UnitCatalog.All)
        {
            var id = unit.Id;
            var target = LiftTarget(id);
            var next = Lift[id] + (target - Lift[id]) * follow;
            Lift = Lift.SetItem(id, Math.Abs(target - next) < 0.02f ? target : next);
        }

        if (IsTweening)
        {
            var t = Math.Clamp((now - _tweenStart) / TweenDuration.TotalSeconds, 0, 1);
            var e = 1 - Math.Pow(1 - t, 3);
            Pose = new CameraPose(
                (float)(_tweenFrom.Yaw + (_tweenTo.Yaw - _tweenFrom.Yaw) * e),
                (float)(_tweenFrom.Elevation + (_tweenTo.Elevation - _tweenFrom.Elevation) * e));
            if (t >= 1) IsTweening = false;
        }
        else if (IsOrbiting)
        {
            Pose = Pose with { Yaw = Pose.Yaw + OrbitRadPerSecond * (float)dt };
        }
    }
}
