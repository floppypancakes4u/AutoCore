namespace AutoCore.Game.Npc;

using AutoCore.Game.Structures;

/// <summary>
/// Throttle / steering / sharp-turn for vehicle ghosts, matching client
/// <c>CVOGVehicle::MoveToTarget3DPoint</c> @ <c>0x004fc650</c> (called from HBAI path/pursue).
/// Retail AI does <b>not</b> lerp pose — it writes vehicle+0x614/0x618/0x61c and lets
/// <c>VehicleAction</c> + Havok drive the chassis and wheels.
/// </summary>
public static class VehicleDriveInputs
{
    /// <summary>
    /// Compute drive axes from chassis facing and a world aim point (XZ).
    /// </summary>
    /// <param name="position">Current chassis position.</param>
    /// <param name="facingYaw">Current chassis yaw (radians, Atan2(x,z) convention).</param>
    /// <param name="aim">World aim (waypoint / pursue target).</param>
    /// <param name="speed">Current ground speed (u/s).</param>
    /// <param name="cruiseThrottle">Throttle when well aligned and moving (0..1).</param>
    public static (float Throttle, float Steering, byte SharpTurn) Compute(
        Vector3 position,
        float facingYaw,
        Vector3 aim,
        float speed,
        float cruiseThrottle = 1f)
    {
        var dx = aim.X - position.X;
        var dz = aim.Z - position.Z;
        var distSq = (dx * dx) + (dz * dz);
        if (distSq < 1e-6f)
            return (0f, 0f, 0);

        var inv = 1f / MathF.Sqrt(distSq);
        var dirX = dx * inv;
        var dirZ = dz * inv;

        // Forward / right in XZ (yaw about +Y, Atan2(x,z) → forward=(sin,cos)).
        var fwdX = MathF.Sin(facingYaw);
        var fwdZ = MathF.Cos(facingYaw);
        var rightX = fwdZ;   // right = (cos, -sin) for this yaw convention? 
        // Right of forward (sin,cos) is (cos, -sin):
        rightX = MathF.Cos(facingYaw);
        var rightZ = -MathF.Sin(facingYaw);

        var forwardDot = (fwdX * dirX) + (fwdZ * dirZ);   // cos heading error
        var lateralDot = (rightX * dirX) + (rightZ * dirZ); // sin heading error (signed)

        // Client: steering from lateral error, clamped [-1,1].
        var steering = Math.Clamp(lateralDot * 1.25f, -1f, 1f);

        // Throttle: full cruise when roughly facing the aim; ease when reverse-facing.
        var thr = cruiseThrottle;
        if (forwardDot < 0.15f)
            thr = cruiseThrottle * Math.Clamp((forwardDot + 0.5f) / 0.65f, 0.15f, 1f);
        if (forwardDot < -0.2f)
            thr = Math.Clamp(forwardDot, -0.5f, 0f); // slight reverse to re-orient if needed

        // Sharp-turn assist when moving fast with large heading error (client +0x61c).
        byte sharp = 0;
        if (speed > 6f && MathF.Abs(lateralDot) > 0.45f && MathF.Abs(forwardDot) > 0.05f)
            sharp = 1;

        return (Math.Clamp(thr, -1f, 1f), steering, sharp);
    }

    public static float YawFromQuaternion(Quaternion q)
    {
        var siny = 2f * ((q.W * q.Y) + (q.X * q.Z));
        var cosy = 1f - (2f * ((q.Y * q.Y) + (q.X * q.X)));
        return MathF.Atan2(siny, cosy);
    }
}
