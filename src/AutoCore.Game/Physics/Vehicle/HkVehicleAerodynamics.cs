namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Stock Havok 2.3 <c>hkDefaultAerodynamics::update</c> force accumulation
/// (<c>autoassault.exe</c> @ 0x64dae0). Writes the body force later applied by the
/// vehicle framework force applicator.
/// <para>
/// Formulas (Ghidra decompile + <c>read_memory</c>):
/// <list type="bullet">
/// <item><c>v = dot(linVel, worldFront)</c></item>
/// <item><c>dragMag = DAT_00aaa6cc * rho * A * Cd * |v| * v</c> (−0.5)</item>
/// <item><c>liftMag = DAT_00a0f298 * rho * A * Cl * v²</c> (+0.5)</item>
/// <item><c>F = dragMag·worldFront + liftMag·worldUp + extraG·mass</c></item>
/// </list>
/// </para>
/// </summary>
public static class HkVehicleAerodynamics
{
    /// <summary>
    /// Compute the total aerodynamics force in world space (xyz).
    /// </summary>
    /// <param name="rho">Air density (component +0x30).</param>
    /// <param name="frontalArea">Frontal area A (component +0x34).</param>
    /// <param name="dragCoefficient">Drag coefficient Cd (component +0x38).</param>
    /// <param name="liftCoefficient">Lift coefficient Cl (component +0x3c); negative → downforce.</param>
    /// <param name="extraGx">Extra gravity acceleration X (component +0x40), world space.</param>
    /// <param name="extraGy">Extra gravity acceleration Y (component +0x44), world space.</param>
    /// <param name="extraGz">Extra gravity acceleration Z (component +0x48), world space.</param>
    /// <param name="worldFrontX">Chassis front axis transformed to world (R * frontAxis).</param>
    /// <param name="worldFrontY">World front Y.</param>
    /// <param name="worldFrontZ">World front Z.</param>
    /// <param name="worldUpX">Chassis up axis transformed to world (R * upAxis).</param>
    /// <param name="worldUpY">World up Y.</param>
    /// <param name="worldUpZ">World up Z.</param>
    /// <param name="linVelX">Chassis linear velocity X (RB+0x40).</param>
    /// <param name="linVelY">Chassis linear velocity Y (RB+0x44).</param>
    /// <param name="linVelZ">Chassis linear velocity Z (RB+0x48).</param>
    /// <param name="mass">Chassis mass (1 / RB+0x2c when invMass ≠ 0; else 0).</param>
    public static (float Fx, float Fy, float Fz) ComputeForce(
        float rho,
        float frontalArea,
        float dragCoefficient,
        float liftCoefficient,
        float extraGx,
        float extraGy,
        float extraGz,
        float worldFrontX,
        float worldFrontY,
        float worldFrontZ,
        float worldUpX,
        float worldUpY,
        float worldUpZ,
        float linVelX,
        float linVelY,
        float linVelZ,
        float mass)
    {
        // fVar21 in decompile: signed forward speed.
        float v = linVelX * worldFrontX + linVelY * worldFrontY + linVelZ * worldFrontZ;

        // dragMag = -0.5 * rho * A * Cd * |v| * v  (DAT_00aaa6cc)
        float dragMag = HkPhysicsConstants.NegHalf * rho * frontalArea * dragCoefficient
                        * MathF.Abs(v) * v;

        // liftMag = +0.5 * rho * A * Cl * v^2  (DAT_00a0f298)
        float liftMag = HkPhysicsConstants.Half * rho * frontalArea * liftCoefficient * v * v;

        float fx = dragMag * worldFrontX + liftMag * worldUpX + extraGx * mass;
        float fy = dragMag * worldFrontY + liftMag * worldUpY + extraGy * mass;
        float fz = dragMag * worldFrontZ + liftMag * worldUpZ + extraGz * mass;

        return (fx, fy, fz);
    }
}
