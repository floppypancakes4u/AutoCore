namespace AutoCore.Game.Npc;

using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Structures;

/// <summary>
/// Post-process <see cref="NpcPathFollower"/> steps so patrol motion is less steppy:
/// limited turn rate, blended height, and carry velocity through zero-wait waypoint arrivals.
/// </summary>
public static class SoftNpcPathMotion
{
    /// <summary>Isolation lever: when true, <see cref="NpcTicker"/> softens path steps.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Max yaw change (rad/s) toward path heading.</summary>
    public static float MaxYawRateRadiansPerSecond { get; set; } = MathF.PI; // 180°/s

    /// <summary>How quickly Y approaches the path height (1/s exponential-ish via clamp lerp).</summary>
    public static float YBlendPerSecond { get; set; } = 3f;

    /// <summary>
    /// Soften a hard path step. Pure: no entity mutation. When <see cref="Enabled"/> is false, returns <paramref name="hard"/> unchanged.
    /// </summary>
    public static PathStepResult Apply(
        PathStepResult hard,
        Vector3 previousPosition,
        Quaternion previousRotation,
        float speed,
        float dt,
        MapPathTemplate path,
        long nowMs)
    {
        if (!Enabled || dt <= 0f || path == null || path.Points.Count == 0)
            return hard;

        var result = hard;

        // Blend vertical so hills don't teleport Y every tick.
        var yAlpha = Math.Clamp(YBlendPerSecond * dt, 0f, 1f);
        result.NewPosition = new Vector3(
            hard.NewPosition.X,
            previousPosition.Y + ((hard.NewPosition.Y - previousPosition.Y) * yAlpha),
            hard.NewPosition.Z);

        // Limit turn rate toward the follower's desired facing.
        result.Rotation = LimitYaw(previousRotation, hard.Rotation, dt);

        // Carry speed through instantaneous waypoint arrivals with no wait (avoid stop-start).
        if (hard.Arrived && hard.WaitUntilMs <= nowMs && speed > 0f)
        {
            var next = path.Points[Math.Clamp(hard.NewIndex, 0, path.Points.Count - 1)].Position;
            var dx = next.X - result.NewPosition.X;
            var dz = next.Z - result.NewPosition.Z;
            var dist = MathF.Sqrt((dx * dx) + (dz * dz));
            if (dist > 1e-3f)
            {
                var inv = 1f / dist;
                result.Velocity = new Vector3(dx * inv * speed, 0f, dz * inv * speed);
                // Face travel direction (still rate-limited above from previousRotation).
                result.Rotation = LimitYaw(previousRotation, YawQuaternion(dx, dz), dt);
            }
        }

        return result;
    }

    internal static Quaternion LimitYaw(Quaternion from, Quaternion to, float dt)
    {
        var fromYaw = YawFromQuaternion(from);
        var toYaw = YawFromQuaternion(to);
        var delta = NormalizeRadians(toYaw - fromYaw);
        var maxStep = Math.Max(0f, MaxYawRateRadiansPerSecond) * Math.Max(dt, 1e-4f);
        if (MathF.Abs(delta) <= maxStep)
            return to;

        var stepped = fromYaw + Math.Clamp(delta, -maxStep, maxStep);
        return YawQuaternionFromYaw(stepped);
    }

    internal static float YawFromQuaternion(Quaternion q)
    {
        var siny = 2f * ((q.W * q.Y) + (q.X * q.Z));
        var cosy = 1f - (2f * ((q.Y * q.Y) + (q.X * q.X)));
        return MathF.Atan2(siny, cosy);
    }

    internal static float NormalizeRadians(float a)
    {
        while (a > MathF.PI)
            a -= MathF.PI * 2f;
        while (a < -MathF.PI)
            a += MathF.PI * 2f;
        return a;
    }

    private static Quaternion YawQuaternion(float dx, float dz)
    {
        var yaw = MathF.Atan2(dx, dz);
        return YawQuaternionFromYaw(yaw);
    }

    private static Quaternion YawQuaternionFromYaw(float yaw)
    {
        var half = yaw * 0.5f;
        return new Quaternion(0f, MathF.Sin(half), 0f, MathF.Cos(half));
    }
}
