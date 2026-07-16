namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Pure math port of continuous AVD —
/// <c>hkAngularVelocityDamper_update @ 0x64d810</c> (Havok action, every sim step).
/// Collision branch is speed-triggered (|w| vs threshold), not the 6400 ms collision timer.
/// </summary>
public static class HkVehicleVelocityDamper
{
    /// <summary>
    /// Dampen angular velocity for one step.
    /// <list type="bullet">
    /// <item>Gate: <c>wx²+wy²+wz² &lt;= threshold²</c> → <paramref name="normalDamp"/>, else <paramref name="collisionDamp"/>.</item>
    /// <item>Scale: <c>f = max(0, 1 − rate·dt)</c>; all four components (xyz + w slot) are multiplied by f.</item>
    /// </list>
    /// The 4th slot is scaled but not included in the |w| gate (matches rb+0x50..0x5c).
    /// </summary>
    public static (float Wx, float Wy, float Wz, float Ww) DampenAngular(
        float wx,
        float wy,
        float wz,
        float ww,
        float dt,
        float normalDamp,
        float collisionDamp,
        float threshold)
    {
        float w2 = wx * wx + wy * wy + wz * wz;
        float thr2 = threshold * threshold;
        float rate = w2 <= thr2 ? normalDamp : collisionDamp;
        float f = 1f - rate * dt;
        if (f < 0f)
            f = 0f;

        return (wx * f, wy * f, wz * f, ww * f);
    }
}
