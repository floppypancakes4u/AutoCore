namespace AutoCore.Game.Combat;

using AutoCore.Game.Structures;

/// <summary>
/// Per-weapon TacArc geometry. Client stores <c>WeaponSpecific.ValidArc</c> as the
/// <b>cosine of the cone half-angle</b> (not degrees). In-cone test matches
/// <c>FUN_004e8930</c> / <c>FindDistanceToTarget</c>: horizontal unit vectors, strict
/// <c>dot &gt; ValidArc</c>.
/// </summary>
public static class TacArcGeometry
{
    /// <summary>Client OnFire secondary falloff base <c>DAT_009d3364</c>.</summary>
    public const float SprayFalloffBase = 1.05f;

    /// <summary>
    /// Horizontal aim unit from yaw (radians): X = sin(yaw), Z = cos(yaw), Y = 0.
    /// Matches vehicle yaw extraction used by server pose.
    /// </summary>
    public static Vector3 AimFromYaw(float yawRadians)
    {
        return new Vector3(MathF.Sin(yawRadians), 0f, MathF.Cos(yawRadians));
    }

    /// <summary>
    /// Vehicle forward yaw from a unit quaternion (yaw about Y).
    /// Same formula as <c>Vehicle.YawFromQuaternion</c>.
    /// </summary>
    public static float YawFromQuaternion(float x, float y, float z, float w)
    {
        var siny = 2f * ((w * y) + (x * z));
        var cosy = 1f - (2f * ((y * y) + (x * x)));
        return MathF.Atan2(siny, cosy);
    }

    /// <summary>
    /// True if <paramref name="target"/> lies inside the TacArc of <paramref name="validArc"/>
    /// about horizontal <paramref name="aimUnit"/> from <paramref name="shooter"/>.
    /// </summary>
    public static bool IsInArc(Vector3 shooter, Vector3 aimUnit, Vector3 target, float validArc)
    {
        // ValidArc <= -1 → full 360° (mines); always in-arc.
        if (validArc <= -1f)
            return true;

        var dx = target.X - shooter.X;
        var dz = target.Z - shooter.Z;
        var lenSq = dx * dx + dz * dz;
        if (lenSq < 1e-8f)
            return true; // same horizontal cell — treat as in-arc

        var inv = 1f / MathF.Sqrt(lenSq);
        var dirX = dx * inv;
        var dirZ = dz * inv;

        // Normalize aim on XZ (caller should pass horizontal unit, but be safe).
        var ax = aimUnit.X;
        var az = aimUnit.Z;
        var aLenSq = ax * ax + az * az;
        if (aLenSq < 1e-8f)
            return false;
        if (MathF.Abs(aLenSq - 1f) > 1e-4f)
        {
            var invA = 1f / MathF.Sqrt(aLenSq);
            ax *= invA;
            az *= invA;
        }

        var dot = ax * dirX + az * dirZ;
        // Client: in cone when ValidArc < dot  (strict)
        return validArc < dot;
    }

    public static bool IsInRange(float dist, float rangeMin, float rangeMax)
    {
        if (rangeMin > 0f && dist < rangeMin)
            return false;
        // RangeMax <= 0: no maximum gate (melee / special ordnance).
        if (rangeMax > 0f && dist > rangeMax)
            return false;
        return true;
    }

    /// <summary>
    /// Secondary cone/splash falloff from primary impact (client OnFire @ 0x56e000).
    /// Primary always 1.0. Secondary: max(0, 1.05 - dist/rangeMax); no upper clamp.
    /// </summary>
    public static float SprayFalloff(bool isSprayTarget, float distFromPrimary, float rangeMax)
    {
        if (!isSprayTarget)
            return 1f;
        if (rangeMax <= 0f)
            return 1f;
        return MathF.Max(0f, SprayFalloffBase - distFromPrimary / rangeMax);
    }
}
