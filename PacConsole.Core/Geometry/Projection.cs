namespace PacConsole.Core.Geometry;

/// <summary>Orbit camera pose: yaw around the vertical axis and elevation above the floor, in radians.</summary>
public readonly record struct CameraPose(float Yaw, float Elevation)
{
    public const float MinElevation = 0.08f;
    public const float MaxElevation = 1.52f;

    public static readonly CameraPose Iso = new(0.61f, 0.49f);
    public static readonly CameraPose Front = new(0f, 0.10f);
    public static readonly CameraPose Plan = new(0f, 1.50f);

    public CameraPose Clamped() => this with { Elevation = Math.Clamp(Elevation, MinElevation, MaxElevation) };

    /// <summary>Wraps an angle to (-π, π], so tweens take the short way round.</summary>
    public static float Wrap(float angle)
    {
        var a = (angle + MathF.PI) % MathF.Tau;
        if (a < 0) a += MathF.Tau;
        return a - MathF.PI;
    }

    /// <summary>True when this pose is within <paramref name="tolerance"/> rad of <paramref name="other"/> on both axes.</summary>
    public bool IsNear(CameraPose other, float tolerance = 0.03f) =>
        MathF.Abs(Wrap(Yaw - other.Yaw)) < tolerance && MathF.Abs(Elevation - other.Elevation) < tolerance;

    /// <summary>Yaw in whole degrees, 0-359, for the viewport caption.</summary>
    public int YawDegrees => (int)MathF.Round(((Yaw * 180 / MathF.PI) % 360 + 360) % 360) % 360;

    public int ElevationDegrees => (int)MathF.Round(Elevation * 180 / MathF.PI);
}

public sealed record CameraPreset(string Key, string Label, string Detail, CameraPose Pose);

public static class CameraPresets
{
    public static readonly ImmutableArray<CameraPreset> All =
    [
        new("iso", "Isometric", "Yaw 35° · el 28°", CameraPose.Iso),
        new("elevation", "Elevation", "Yaw 0° · el 6°", CameraPose.Front),
        new("plan", "Plan", "Yaw 0° · el 86°", CameraPose.Plan),
    ];
}

/// <summary>
/// Orthographic orbit projection. Screen X follows the yawed X axis; screen Y combines height
/// (scaled by cos elevation) and depth (scaled by sin elevation), so the camera looks down at the plant.
/// </summary>
public readonly struct OrbitProjection
{
    private readonly float _cosYaw, _sinYaw, _cosElev, _sinElev;

    public OrbitProjection(CameraPose pose, float scale, float centerX, float centerY)
    {
        _cosYaw = MathF.Cos(pose.Yaw);
        _sinYaw = MathF.Sin(pose.Yaw);
        _cosElev = MathF.Cos(pose.Elevation);
        _sinElev = MathF.Sin(pose.Elevation);
        Scale = scale;
        CenterX = centerX;
        CenterY = centerY;
    }

    public float Scale { get; }
    public float CenterX { get; }
    public float CenterY { get; }

    public float SinYaw => _sinYaw;
    public float CosYaw => _cosYaw;
    public float SinElevation => _sinElev;
    public float CosElevation => _cosElev;

    /// <summary>Distance toward the viewer: larger is nearer.</summary>
    public float Depth(Vec3 p) => (p.X * _sinYaw + p.Z * _cosYaw) * _cosElev + p.Y * _sinElev;

    /// <summary>Screen offset of a rise of <paramref name="height"/> (negative: up the screen).</summary>
    public float Rise(float height) => -height * _cosElev * Scale;

    public Vec2 Project(Vec3 p)
    {
        var xr = p.X * _cosYaw - p.Z * _sinYaw;
        var zr = p.X * _sinYaw + p.Z * _cosYaw;
        return new(CenterX + Scale * xr, CenterY + Scale * (-p.Y * _cosElev + zr * _sinElev));
    }
}
