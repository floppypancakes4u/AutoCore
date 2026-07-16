namespace AutoCore.Game.Physics.Vehicle;

/// <summary>
/// Wheel / chassis cast query used by the vehicle sim.
/// Retail client casts against the full Havok broadphase (all bodies). Server v1 typically
/// implements terrain heightfield; world collision geometry can be plugged in later without
/// changing suspension/friction math.
/// </summary>
public interface IVehicleCollisionQuery
{
    /// <summary>
    /// Cast a ray from <paramref name="origin"/> along <paramref name="direction"/> (usually down).
    /// <paramref name="maxDistance"/> is hardpoint rest length + radius style length.
    /// Returns true on hit; hit fraction in [0,1] relative to maxDistance when applicable.
    /// </summary>
    bool CastRay(
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance,
        out VehicleRayHit hit);
}

/// <summary>Result of a vehicle wheel/chassis ray cast.</summary>
public readonly struct VehicleRayHit
{
    public VehicleRayHit(
        float fraction,
        float pointX, float pointY, float pointZ,
        float normalX, float normalY, float normalZ,
        bool isTerrain)
    {
        Fraction = fraction;
        PointX = pointX;
        PointY = pointY;
        PointZ = pointZ;
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
        IsTerrain = isTerrain;
    }

    /// <summary>Hit fraction along the cast (0 = origin, 1 = full max distance).</summary>
    public float Fraction { get; }
    public float PointX { get; }
    public float PointY { get; }
    public float PointZ { get; }
    public float NormalX { get; }
    public float NormalY { get; }
    public float NormalZ { get; }
    /// <summary>True if hit terrain heightfield; false if static/dynamic geometry body.</summary>
    public bool IsTerrain { get; }
}

/// <summary>
/// Null query — always misses (airborne). Useful for pure unit tests of suspension/aero.
/// </summary>
public sealed class NullVehicleCollisionQuery : IVehicleCollisionQuery
{
    public static readonly NullVehicleCollisionQuery Instance = new();

    public bool CastRay(
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance,
        out VehicleRayHit hit)
    {
        hit = default;
        return false;
    }
}
