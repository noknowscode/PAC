using PacConsole.Core.Geometry;
using PacConsole.Core.Plant;

namespace PacConsole.Tests;

/// <summary>The plant view's camera: orbit, tweens, drags, keys' steps and the selected unit's lift.</summary>
public class CameraRigTests
{
    private const double Frame = 1 / 60.0;

    private static CameraRig Rig(bool animations = true) => new(animations);

    /// <summary>Runs frames until the rig is still or <paramref name="seconds"/> pass; returns the clock.</summary>
    private static double Run(CameraRig rig, double now, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            now += Frame;
            rig.Step(Frame, now);
        }
        return now;
    }

    [Test]
    public void It_opens_still_in_the_isometric_view_with_no_unit_lifted()
    {
        var rig = Rig();

        rig.Pose.Should().Be(CameraPose.Iso);
        rig.RestingPreset!.Key.Should().Be("iso");
        rig.IsMoving.Should().BeFalse("orbiting is opt-in");
        rig.Status.Should().Be("Auto orbit");
        rig.Lift.Values.Should().OnlyContain(l => l == 0);
    }

    [Test]
    public void Auto_orbit_turns_at_its_rate_and_pauses_while_held()
    {
        var rig = Rig();
        rig.SetAuto(true);
        rig.Status.Should().Be("Orbiting");

        Run(rig, 0, 1);
        rig.Pose.Yaw.Should().BeApproximately(CameraPose.Iso.Yaw + CameraRig.OrbitRadPerSecond, 0.01f);
        rig.RestingPreset.Should().BeNull();

        rig.IsHeld = true;
        var yaw = rig.Pose.Yaw;
        Run(rig, 1, 1);
        rig.Pose.Yaw.Should().Be(yaw, "a pointer over the plant or focus on it pauses the orbit");
        rig.Status.Should().Be("Orbit paused");
        rig.IsMoving.Should().BeFalse();
    }

    [Test]
    public void A_preset_eases_there_in_600_ms_the_short_way_round_and_stops_the_orbit()
    {
        var rig = Rig();
        rig.SetAuto(true);
        var elevation = CameraPresets.All.Single(p => p.Key == "elevation");

        rig.GoTo(elevation, now: 10);

        rig.IsAuto.Should().BeFalse();
        rig.IsTweening.Should().BeTrue();
        rig.Step(Frame, 10.3);
        rig.Pose.Should().NotBe(elevation.Pose, "half way");
        rig.Step(Frame, 10.65);
        rig.IsTweening.Should().BeFalse();
        rig.Pose.IsNear(elevation.Pose).Should().BeTrue();
        rig.RestingPreset.Should().Be(elevation);
    }

    [Test]
    public void A_tween_never_turns_more_than_half_a_turn()
    {
        var rig = Rig();
        rig.TweenTo(new CameraPose(CameraPose.Iso.Yaw + 2 * MathF.Tau + 0.2f, CameraPose.Iso.Elevation), 0);
        rig.Step(Frame, 1);

        rig.Pose.Yaw.Should().BeApproximately(CameraPose.Iso.Yaw + 0.2f, 1e-4f, "whole turns are dropped");
    }

    [Test]
    public void Rotate_and_tilt_step_from_where_the_camera_is_heading_and_tilt_is_kept_in_range()
    {
        var rig = Rig();

        rig.Rotate(1, 0);
        rig.Rotate(1, 0.1);
        Run(rig, 0.1, 1);
        rig.Pose.Yaw.Should().BeApproximately(CameraPose.Iso.Yaw + MathF.PI / 2, 1e-4f, "a second press mid-tween adds to the first");

        for (var i = 0; i < 20; i++) rig.Tilt(1, 2);
        Run(rig, 2, 1);
        rig.Pose.Elevation.Should().BeApproximately(CameraPose.MaxElevation, 1e-5f);
    }

    [Test]
    public void The_toggle_ends_a_tween_where_the_camera_is()
    {
        var rig = Rig();
        rig.Rotate(1, 0);
        rig.Step(Frame, 0.2);
        var mid = rig.Pose;

        rig.SetAuto(false);

        rig.IsTweening.Should().BeFalse();
        rig.Step(Frame, 0.4);
        rig.Pose.Should().Be(mid);
    }

    [Test]
    public void A_drag_starts_past_the_threshold_and_orbits_from_the_pressed_pose()
    {
        var rig = Rig();
        rig.SetAuto(true);
        rig.Press();

        rig.DragBy(2, 1, touch: false).Should().Be(DragStep.Pending, "a small move is still a click");
        rig.IsAuto.Should().BeTrue();
        rig.DragBy(50, -20, touch: false).Should().Be(DragStep.Started);
        rig.IsAuto.Should().BeFalse("a drag takes the camera from the orbit");
        rig.Status.Should().Be("Auto orbit");
        rig.DragBy(100, -20, touch: false).Should().Be(DragStep.Moved);

        rig.Pose.Yaw.Should().BeApproximately(CameraPose.Iso.Yaw + 100 * CameraRig.DragYawPerPx, 1e-5f);
        rig.Pose.Elevation.Should().BeApproximately(CameraPose.Iso.Elevation - 20 * CameraRig.DragElevationPerPx, 1e-5f);
        rig.EndDrag().Should().BeTrue();
        rig.EndDrag().Should().BeFalse();
    }

    [Test]
    public void A_finger_drag_that_is_mostly_vertical_is_the_page_s_scroll_and_a_sideways_one_turns_yaw_only()
    {
        var rig = Rig();
        rig.Press();

        rig.DragBy(5, 30, touch: true).Should().Be(DragStep.Scroll);
        rig.IsDragging.Should().BeFalse();

        rig.Press();
        rig.DragBy(40, 10, touch: true).Should().Be(DragStep.Started);
        rig.Pose.Elevation.Should().Be(CameraPose.Iso.Elevation, "a touch orbits on yaw only");
    }

    [Test]
    public void The_selected_unit_lifts_off_its_pad_and_the_last_one_settles_back()
    {
        var rig = Rig();
        rig.Selected = UnitId.Trb;
        rig.IsMoving.Should().BeTrue("a lift to make");

        Run(rig, 0, 0.28);
        rig.Lift[UnitId.Trb].Should().BeApproximately(CameraRig.FullLift * (1 - MathF.Exp(-1)), 0.3f, "one time constant");
        Run(rig, 0.28, 3);
        rig.Lift[UnitId.Trb].Should().Be(CameraRig.FullLift);
        rig.LiftSettled.Should().BeTrue();

        rig.Selected = UnitId.Gen;
        Run(rig, 4, 3);
        rig.Lift[UnitId.Trb].Should().Be(0);
        rig.Lift[UnitId.Gen].Should().Be(CameraRig.FullLift);
        rig.IsMoving.Should().BeFalse();
    }

    [Test]
    public void With_animations_off_everything_arrives_at_once()
    {
        var rig = Rig(animations: false);
        rig.Selected = UnitId.Clg;

        rig.Settle();
        rig.GoTo(CameraPresets.All.Single(p => p.Key == "plan"), 0);

        rig.Lift[UnitId.Clg].Should().Be(CameraRig.FullLift);
        rig.IsTweening.Should().BeFalse();
        rig.RestingPreset!.Key.Should().Be("plan");
        rig.IsMoving.Should().BeFalse();
    }

    [Test]
    public void The_motion_clock_counts_every_frame()
    {
        var rig = Rig();
        rig.Step(0.05, 0.05);
        rig.Step(0.05, 0.1);

        rig.Seconds.Should().BeApproximately(0.1, 1e-9);
    }
}
