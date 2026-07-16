namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Stock Havok 2.3 <c>hkDefaultSuspension::update</c> spring+damper scalar force
/// (<c>autoassault.exe</c> @ 0x64de50). Output is the per-wheel force written to susp+0x34[i]
/// and later applied along the contact normal in <c>postTickApplyForces</c> (0x64bc70).
/// </summary>
public static class HkVehicleSuspension
{
    /// <summary>
    /// Compute one wheel's suspension force magnitude.
    /// </summary>
    /// <param name="inContact">Wheel contact flag (wheel+0x80). Airborne → 0.</param>
    /// <param name="restLength">Rest length (comp+0x28[i]).</param>
    /// <param name="strength">Spring strength (comp+0x44[i]).</param>
    /// <param name="dampCompression">Compression damping (comp+0x50[i]).</param>
    /// <param name="dampExtension">Extension/rebound damping (comp+0x5C[i]).</param>
    /// <param name="currentLength">Current suspension length (wheel+0xB0).</param>
    /// <param name="scalingFactor">Per-wheel scale (wheel+0xAC), often 1/maxSuspLen.</param>
    /// <param name="closingSpeed">Closing speed (wheel+0xB4); &lt;0 compress, &gt;=0 extend.</param>
    /// <param name="invMass">
    /// Chassis rigid-body scalar at RB+0x2c. Decompile uses
    /// <c>gScale = (bodyScalar == 0) ? 0 : 1 / bodyScalar</c>.
    /// </param>
    public static float ComputeForce(
        bool inContact,
        float restLength,
        float strength,
        float dampCompression,
        float dampExtension,
        float currentLength,
        float scalingFactor,
        float closingSpeed,
        float invMass)
    {
        if (!inContact)
            return 0f;

        // fVar6 = 1 / RB[+0x2c] when non-zero (hkDefaultSuspension_update @ 0x64de50).
        float invMassNormalizer = invMass == 0f ? 0f : 1f / invMass;

        // closingSpeed >= 0 → extension (rebound); else compression.
        float damp = closingSpeed >= 0f ? dampExtension : dampCompression;

        float force = ((restLength - currentLength) * strength * scalingFactor - damp * closingSpeed)
                      * invMassNormalizer;

        // Server stability: unbounded spring when deeply penetrating flips / launches chassis.
        if (force > HkPhysicsConstants.MaxSuspensionForce)
            force = HkPhysicsConstants.MaxSuspensionForce;
        else if (force < -HkPhysicsConstants.MaxSuspensionForce)
            force = -HkPhysicsConstants.MaxSuspensionForce;

        return force;
    }
}
