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
        new("iso", "ISO · NE", "YAW 35° / EL 28°", CameraPose.Iso),
        new("elevation", "ELEVATION · S", "YAW 0° / EL 6°", CameraPose.Front),
        new("plan", "PLAN", "YAW 0° / EL 86°", CameraPose.Plan),
    ];
}

/// <summary>
/// Orthographic orbit projection. Screen X follows the yawed X axis; screen Y combines height
/// (scaled by cos elevation) and depth (scaled by sin elevation), so the camera looks down at the plant.
/// </summary>
public readonly struct Projection
{
    /// <summary>The prototype's viewport, which the default scale and centre were tuned against.</summary>
    public const float ReferenceWidth = 772, ReferenceHeight = 540;

    private readonly float _cosYaw, _sinYaw, _cosElev, _sinElev;

    public Projection(CameraPose pose, float scale, float centerX, float centerY)
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

    public Vec2 Project(Vec3 p)
    {
        var xr = p.X * _cosYaw - p.Z * _sinYaw;
        var zr = p.X * _sinYaw + p.Z * _cosYaw;
        return new(CenterX + Scale * xr, CenterY + Scale * (-p.Y * _cosElev + zr * _sinElev));
    }

    /// <summary>
    /// The main viewport projection. Scale shrinks as the camera tilts toward plan view so the
    /// platform's depth still fits, exactly as in the prototype, then scales with the viewport.
    /// </summary>
    public static Projection ForViewport(CameraPose pose, float width, float height)
    {
        var ce = MathF.Cos(pose.Elevation);
        var se = MathF.Sin(pose.Elevation);
        var k = MathF.Min(width / ReferenceWidth, height / ReferenceHeight);
        var scale = MathF.Min(2.3f, 205f / (126.5f * se + 20f * ce)) * k;
        var centerY = height / 2 + (300 + 40 * ce - ReferenceHeight / 2) * k;
        return new Projection(pose, scale, width / 2, centerY);
    }

    /// <summary>Fits every segment inside a box, for whole-plant thumbnails.</summary>
    public static Projection Fit(CameraPose pose, IEnumerable<Segment3> segments, float width, float height, float padding)
    {
        var unit = new Projection(pose, 1, 0, 0);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var s in segments)
        {
            foreach (var p in new[] { unit.Project(s.A), unit.Project(s.B) })
            {
                minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
                minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
            }
        }
        var scale = MathF.Min((width - 2 * padding) / (maxX - minX), (height - 2 * padding) / (maxY - minY));
        return new Projection(pose, scale, width / 2 - scale * (minX + maxX) / 2, height / 2 - scale * (minY + maxY) / 2);
    }

    /// <summary>
    /// Centres one unit using its bounding sphere, so the scale stays constant while the unit rotates
    /// (a bounding-box fit would breathe in and out every frame).
    /// </summary>
    public static Projection AroundSphere(CameraPose pose, Vec3 center, float radius, float width, float height, float padding)
    {
        var scale = (MathF.Min(width, height) / 2 - padding) / radius;
        var c = new Projection(pose, scale, 0, 0).Project(center);
        return new Projection(pose, scale, width / 2 - c.X, height / 2 - c.Y);
    }
}
