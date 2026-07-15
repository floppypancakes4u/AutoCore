namespace AutoCore.Game.Npc;

using AutoCore.Game.Structures;

/// <summary>
/// Relative turret yaw for ghost <c>WantedTurretDirection</c> (client vehicle+0x15c).
/// Client applies the float as an axis-angle on top of chassis orientation
/// (<c>FUN_004F9030</c> / <c>FUN_00567CE0</c>), so this is chassis-relative, wrapped to
/// <c>[0, 2π)</c> (client wrap constant <c>DAT_00af18a0</c>).
/// </summary>
public static class VehicleTurretAim
{
    private const float EpsilonSq = 1e-6f;

    /// <summary>
    /// Compute wanted turret direction from chassis pose to a world target point (XZ).
    /// </summary>
    public static float ComputeWantedDirection(
        Vector3 vehiclePosition,
        Quaternion vehicleRotation,
        Vector3 targetPosition)
    {
        var dx = targetPosition.X - vehiclePosition.X;
        var dz = targetPosition.Z - vehiclePosition.Z;
        if ((dx * dx) + (dz * dz) < EpsilonSq)
            return 0f;

        var aimYaw = MathF.Atan2(dx, dz);
        var facingYaw = VehicleDriveInputs.YawFromQuaternion(vehicleRotation);
        return NormalizeToTwoPi(aimYaw - facingYaw);
    }

    /// <summary>Wrap angle into <c>[0, 2π)</c>.</summary>
    public static float NormalizeToTwoPi(float radians)
    {
        var twoPi = MathF.PI * 2f;
        var r = radians % twoPi;
        if (r < 0f)
            r += twoPi;
        return r;
    }
}
