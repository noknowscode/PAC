namespace PacConsole.Core.Geometry;

/// <summary>A point in plant space. Y is up; units are roughly metres.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>A point on screen, in device-independent pixels.</summary>
public readonly record struct Vec2(float X, float Y);

/// <summary>One wireframe edge.</summary>
public readonly record struct Segment3(Vec3 A, Vec3 B);
