namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Wheel spin kinematics from client preUpdate <c>0x64cf20</c> Loop 3:
/// <c>ω = (longContactVel + chassisLongVel) / radius</c>,
/// <c>angle += dt · ω</c>.
/// Evidence: docs/reconstruction/physics/verified/fn_0064cf20_preUpdate.md
/// </summary>
public static class HkVehicleWheelKinematics
{
    /// <summary>
    /// Spin speed (rad/s) from longitudinal velocities and wheel radius.
    /// Non-positive <paramref name="radius"/> yields 0.
    /// </summary>
    public static float ComputeSpinSpeed(float longContactVel, float chassisLongVel, float radius)
    {
        if (radius <= 0f)
            return 0f;
        return (longContactVel + chassisLongVel) / radius;
    }

    /// <summary>
    /// Advances spin angle by <c>dt · spinSpeed</c>. Zero/non-positive <paramref name="dt"/> leaves angle unchanged.
    /// </summary>
    public static float IntegrateSpinAngle(float spinAngle, float spinSpeed, float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f)
            return spinAngle;
        return spinAngle + dt * spinSpeed;
    }

    /// <summary>
    /// Writes spin speed and optionally advances angle.
    /// When <paramref name="integrate"/> is false, zeros spin and leaves angle unchanged.
    /// </summary>
    public static void IntegrateSpin(
        ref float spin,
        ref float angle,
        float longContactVel,
        float chassisLongVel,
        float radius,
        float dt,
        bool integrate)
    {
        if (!integrate)
        {
            spin = 0f;
            return;
        }

        spin = ComputeSpinSpeed(longContactVel, chassisLongVel, radius);
        angle = IntegrateSpinAngle(angle, spin, dt);
    }
}
