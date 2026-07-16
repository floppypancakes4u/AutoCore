namespace AutoCore.Game.Physics.Vehicle;

using System.Numerics;

/// <summary>
/// Pure float / xyz vector helpers for vehicle physics.
/// Isolated from game structures so callers may use <see cref="Vector3"/> or raw components.
/// Other modules may duplicate similar math — intentional for decoupling.
/// </summary>
public static class PhysicsMath
{
    /// <summary>Dot product of two 3-vectors.</summary>
    public static float Dot(Vector3 a, Vector3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Dot product from raw components.</summary>
    public static float Dot(float ax, float ay, float az, float bx, float by, float bz)
        => ax * bx + ay * by + az * bz;

    /// <summary>Cross product a × b (right-handed).</summary>
    public static Vector3 Cross(Vector3 a, Vector3 b)
        => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    /// <summary>Cross product from raw components into <paramref name="rx"/>/<paramref name="ry"/>/<paramref name="rz"/>.</summary>
    public static void Cross(
        float ax, float ay, float az,
        float bx, float by, float bz,
        out float rx, out float ry, out float rz)
    {
        rx = ay * bz - az * by;
        ry = az * bx - ax * bz;
        rz = ax * by - ay * bx;
    }

    /// <summary>Euclidean length of a 3-vector.</summary>
    public static float Length(Vector3 v)
        => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary>Euclidean length from raw components.</summary>
    public static float Length(float x, float y, float z)
        => MathF.Sqrt(x * x + y * y + z * z);

    /// <summary>
    /// Unit-length copy of <paramref name="v"/>. Zero-length input returns zero (no NaN).
    /// </summary>
    public static Vector3 Normalize(Vector3 v)
    {
        float len = Length(v);
        if (len <= 0f)
            return Vector3.Zero;
        float inv = 1f / len;
        return new Vector3(v.X * inv, v.Y * inv, v.Z * inv);
    }

    /// <summary>
    /// Unit-length components. Zero-length input writes zeros (no NaN).
    /// </summary>
    public static void Normalize(float x, float y, float z, out float nx, out float ny, out float nz)
    {
        float len = Length(x, y, z);
        if (len <= 0f)
        {
            nx = 0f;
            ny = 0f;
            nz = 0f;
            return;
        }

        float inv = 1f / len;
        nx = x * inv;
        ny = y * inv;
        nz = z * inv;
    }

    /// <summary>Clamp <paramref name="value"/> into [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
