namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Wheel ray cast and suspension-length compression for hk vehicle preUpdate.
/// Primary addresses: <c>hkVehicleWheelCollide::collide</c> 0x64bbd0 (packs ray + phantom cast),
/// compression write in <c>hkVehicleFramework_preUpdate</c> 0x64cf20.
/// </summary>
/// <remarks>
/// Compression / current length (wheel+0xB0):
/// <c>(wheelRadius + suspensionRestLength) · hitFraction − wheelRadius</c>.
/// Miss: length = restLen, fraction = 1, inContact = false, normal = −downAxis,
/// ClosingSpeed = 0.
/// <para>
/// ClosingSpeed (wheel+0xB4 damper input) on hit:
/// <c>dot(chassisLinVel, contactNormal)</c>. Contact normal points out of the surface
/// (up for flat ground). Approaching ground → negative → compression damp in
/// <see cref="HkVehicleSuspension"/>; separating → positive → extension damp.
/// Retail preUpdate also has a deep hitDist branch that scales a point-velocity Δ;
/// this port uses the chassis linear-velocity projection so the damper is active
/// for typical grounded contact (shallow branch would write 0 in pure retail).
/// </para>
/// Evidence: docs/reconstruction/physics/verified/fn_0064cf20_preUpdate.md,
/// docs/reconstruction/physics/0.4-suspension.md
/// </remarks>
public static class HkVehicleWheelCollide
{
    /// <summary>
    /// Damper closing speed: projection of chassis linear velocity onto contact normal.
    /// Negative when moving into the surface (compressing); positive when separating.
    /// </summary>
    public static float ComputeClosingSpeed(
        float velX, float velY, float velZ,
        float normalX, float normalY, float normalZ)
        => velX * normalX + velY * normalY + velZ * normalZ;

    /// <summary>
    /// Cast one wheel from hardpoint along suspension down direction.
    /// Cast length is <paramref name="radius"/> + <paramref name="restLen"/> (retail ray end).
    /// Optional chassis linear velocity fills <see cref="WheelContact.ClosingSpeed"/> on hit.
    /// </summary>
    public static WheelContact CastWheel(
        IVehicleCollisionQuery query,
        float hardpointX, float hardpointY, float hardpointZ,
        float downDirX, float downDirY, float downDirZ,
        float radius,
        float restLen,
        float velX = 0f, float velY = 0f, float velZ = 0f)
    {
        var maxDistance = radius + restLen;

        if (query == null
            || maxDistance <= 0f
            || !query.CastRay(
                hardpointX, hardpointY, hardpointZ,
                downDirX, downDirY, downDirZ,
                maxDistance,
                out var hit))
        {
            return WheelContact.Miss(restLen, downDirX, downDirY, downDirZ);
        }

        // 0x64cf20: wheel+0xB0 = (radius + suspLen) * fraction − radius
        var length = maxDistance * hit.Fraction - radius;
        var closingSpeed = ComputeClosingSpeed(
            velX, velY, velZ,
            hit.NormalX, hit.NormalY, hit.NormalZ);

        return new WheelContact(
            inContact: true,
            length: length,
            fraction: hit.Fraction,
            normalX: hit.NormalX,
            normalY: hit.NormalY,
            normalZ: hit.NormalZ,
            closingSpeed: closingSpeed);
    }
}

/// <summary>
/// Per-wheel contact after cast + compression (subset of client wheel stride 0xC0 fields).
/// </summary>
public readonly struct WheelContact
{
    public WheelContact(
        bool inContact,
        float length,
        float fraction,
        float normalX, float normalY, float normalZ,
        float closingSpeed)
    {
        InContact = inContact;
        Length = length;
        Fraction = fraction;
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
        ClosingSpeed = closingSpeed;
    }

    /// <summary>wheel+0x80 — grounded flag.</summary>
    public bool InContact { get; }

    /// <summary>
    /// wheel+0xB0 — suspension current length:
    /// <c>(radius + restLen) · fraction − radius</c>; miss uses <c>restLen</c>.
    /// </summary>
    public float Length { get; }

    /// <summary>Hit fraction along cast [0,1]; miss = 1.</summary>
    public float Fraction { get; }

    /// <summary>wheel+0x30 contact normal (world); miss = −downAxis.</summary>
    public float NormalX { get; }
    public float NormalY { get; }
    public float NormalZ { get; }

    /// <summary>
    /// wheel+0xB4 closing speed — <c>dot(chassisLinVel, contactNormal)</c> on hit; 0 on miss.
    /// &lt;0 compressing, ≥0 extending (damper coefficient select in <c>0x64de50</c>).
    /// </summary>
    public float ClosingSpeed { get; }

    /// <summary>Miss result: airborne, full rest length, normal = −downDir.</summary>
    public static WheelContact Miss(float restLen, float downDirX, float downDirY, float downDirZ)
        => new(
            inContact: false,
            length: restLen,
            fraction: 1f,
            normalX: -downDirX,
            normalY: -downDirY,
            normalZ: -downDirZ,
            closingSpeed: 0f);
}
