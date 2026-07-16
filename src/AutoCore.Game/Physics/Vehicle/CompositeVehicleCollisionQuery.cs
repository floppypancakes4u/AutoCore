namespace AutoCore.Game.Physics.Vehicle;

using AutoCore.Game.CloneBases;
using AutoCore.Game.Combat;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// CW composite wheel cast: terrain heightfield <b>plus</b> approximate object/vehicle proxy volumes.
/// Retail uses full Havok phantom broadphase (<c>TtPhantom::castRay</c> @ 0x580ed0); this is an
/// intentional improve-not-close stand-in — see <c>docs/reconstruction/physics/IMPLEMENTATION-GAPS.md</c>.
/// </summary>
/// <remarks>
/// Object pass: <see cref="SpatialHashGrid.QueryRadius"/> (XZ) → collidable filter
/// (<see cref="VehicleMapPropRam.IsRamEligibleMapProp"/> for map props, plus other vehicles) →
/// segment-vs-AABB proxy from per-CBID <c>Scale</c> / <c>VehicleSpecific.SkirtExtents</c>.
/// Returns the nearest hit (terrain or object); object hits set <see cref="VehicleRayHit.IsTerrain"/> false.
/// </remarks>
public sealed class CompositeVehicleCollisionQuery : IVehicleCollisionQuery
{
    /// <summary>Default XZ query radius around the wheel hardpoint (cells are 128u).</summary>
    public const float DefaultObjectQueryRadius = 16f;

    /// <summary>Minimum half-extent (m) when Scale/SkirtExtents are missing or zero.</summary>
    public const float DefaultMinHalfExtent = 0.5f;

    private readonly IVehicleCollisionQuery _terrain;
    private readonly SectorMap _map;
    private readonly ClonedObjectBase _excludeSelf;
    private readonly float _queryRadius;

    [ThreadStatic]
    private static List<ClonedObjectBase> _spatialBuffer;

    /// <param name="terrain">Existing heightfield (or flat) query; never null.</param>
    /// <param name="map">Live sector map for spatial object pass; null → terrain only.</param>
    /// <param name="excludeSelf">Casting vehicle (skip own chassis proxy).</param>
    /// <param name="objectQueryRadius">XZ broadphase radius around the ray origin.</param>
    public CompositeVehicleCollisionQuery(
        IVehicleCollisionQuery terrain,
        SectorMap map,
        ClonedObjectBase excludeSelf = null,
        float objectQueryRadius = DefaultObjectQueryRadius)
    {
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        _map = map;
        _excludeSelf = excludeSelf;
        _queryRadius = objectQueryRadius > 0f ? objectQueryRadius : DefaultObjectQueryRadius;
    }

    /// <inheritdoc />
    public bool CastRay(
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance,
        out VehicleRayHit hit)
    {
        hit = default;
        if (maxDistance <= 0f)
            return false;

        var hasTerrain = _terrain.CastRay(
            originX, originY, originZ, dirX, dirY, dirZ, maxDistance, out var terrainHit);

        var hasObject = TryCastObjects(
            originX, originY, originZ, dirX, dirY, dirZ, maxDistance, out var objectHit);

        if (hasTerrain && hasObject)
        {
            hit = objectHit.Fraction < terrainHit.Fraction ? objectHit : terrainHit;
            return true;
        }

        if (hasObject)
        {
            hit = objectHit;
            return true;
        }

        if (hasTerrain)
        {
            hit = terrainHit;
            return true;
        }

        return false;
    }

    private bool TryCastObjects(
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance,
        out VehicleRayHit hit)
    {
        hit = default;
        if (_map?.Grid == null)
            return false;

        var buffer = _spatialBuffer ??= new List<ClonedObjectBase>(64);
        var center = new Vector3(originX, originY, originZ);
        // Expand query slightly by cast length so long suspension rays still see nearby props.
        var radius = MathF.Max(_queryRadius, maxDistance);
        _map.Grid.QueryRadius(center, radius, buffer);

        var deltaX = dirX * maxDistance;
        var deltaY = dirY * maxDistance;
        var deltaZ = dirZ * maxDistance;

        var bestT = float.MaxValue;
        float bestNx = 0f, bestNy = 1f, bestNz = 0f;
        var found = false;

        foreach (var obj in buffer)
        {
            if (!IsWheelCollidable(obj, _excludeSelf))
                continue;
            if (!TryResolveProxyHalfExtents(obj, out var hx, out var hy, out var hz))
                continue;

            var pos = obj.Position;
            if (!TryIntersectAabbSegment(
                    originX, originY, originZ,
                    deltaX, deltaY, deltaZ,
                    pos.X, pos.Y, pos.Z,
                    hx, hy, hz,
                    out var t, out var nx, out var ny, out var nz))
                continue;

            if (t < bestT)
            {
                bestT = t;
                bestNx = nx;
                bestNy = ny;
                bestNz = nz;
                found = true;
            }
        }

        if (!found)
            return false;

        var px = originX + deltaX * bestT;
        var py = originY + deltaY * bestT;
        var pz = originZ + deltaZ * bestT;
        hit = new VehicleRayHit(bestT, px, py, pz, bestNx, bestNy, bestNz, isTerrain: false);
        return true;
    }

    /// <summary>
    /// Map props via <see cref="VehicleMapPropRam.IsRamEligibleMapProp"/>; other live vehicles
    /// for approximate vehicle-vs-vehicle wheel contact. Excludes self / corpses.
    /// </summary>
    internal static bool IsWheelCollidable(ClonedObjectBase obj, ClonedObjectBase excludeSelf)
    {
        if (obj is null || ReferenceEquals(obj, excludeSelf) || obj.IsCorpse)
            return false;

        if (VehicleMapPropRam.IsRamEligibleMapProp(obj))
            return true;

        // Approximate movable vehicle-vs-vehicle (residual: no true hulls).
        return obj is Vehicle;
    }

    /// <summary>
    /// Half-extents for the proxy AABB. Vehicles prefer <c>SkirtExtents</c>; props use scalar
    /// <c>Scale</c> (entity, else clonebase) as full-size edge → half = scale/2.
    /// </summary>
    internal static bool TryResolveProxyHalfExtents(
        ClonedObjectBase obj, out float halfX, out float halfY, out float halfZ)
    {
        halfX = halfY = halfZ = DefaultMinHalfExtent;
        if (obj is null)
            return false;

        var instanceScale = obj.Scale > 1e-4f ? obj.Scale : 1f;

        if (obj is Vehicle)
        {
            if (obj.CloneBaseObject is CloneBaseVehicle cv)
            {
                var se = cv.VehicleSpecific.SkirtExtents;
                if (se.X > 1e-4f || se.Y > 1e-4f || se.Z > 1e-4f)
                {
                    halfX = MathF.Max(se.X, DefaultMinHalfExtent) * instanceScale;
                    halfY = MathF.Max(se.Y, DefaultMinHalfExtent) * instanceScale;
                    halfZ = MathF.Max(se.Z, DefaultMinHalfExtent) * instanceScale;
                    return true;
                }
            }
        }

        // Prop / fallback: treat Scale as full edge length (entity Scale preferred, else CBID).
        var full = obj.Scale;
        if (full <= 1e-4f && obj.CloneBaseObject != null)
            full = obj.CloneBaseObject.SimpleObjectSpecific.Scale;
        if (full <= 1e-4f)
            full = DefaultMinHalfExtent * 2f;

        var half = MathF.Max(full * 0.5f, DefaultMinHalfExtent);
        halfX = halfY = halfZ = half;
        return true;
    }

    /// <summary>
    /// Segment <c>origin + t·delta</c>, <c>t ∈ [0,1]</c>, vs AABB center/half-extents.
    /// On hit: fraction t and unit face normal (synthesized, not mesh-derived).
    /// Origin inside reports t=0 with nearest-face normal.
    /// </summary>
    internal static bool TryIntersectAabbSegment(
        float originX, float originY, float originZ,
        float deltaX, float deltaY, float deltaZ,
        float centerX, float centerY, float centerZ,
        float halfX, float halfY, float halfZ,
        out float t,
        out float normalX, out float normalY, out float normalZ)
    {
        t = 0f;
        normalX = 0f;
        normalY = 1f;
        normalZ = 0f;

        halfX = MathF.Max(halfX, 1e-6f);
        halfY = MathF.Max(halfY, 1e-6f);
        halfZ = MathF.Max(halfZ, 1e-6f);

        var minX = centerX - halfX;
        var maxX = centerX + halfX;
        var minY = centerY - halfY;
        var maxY = centerY + halfY;
        var minZ = centerZ - halfZ;
        var maxZ = centerZ + halfZ;

        // Origin already inside → contact at t=0, normal from nearest face.
        if (originX >= minX && originX <= maxX
            && originY >= minY && originY <= maxY
            && originZ >= minZ && originZ <= maxZ)
        {
            NearestFaceNormal(originX, originY, originZ, centerX, centerY, centerZ, halfX, halfY, halfZ,
                out normalX, out normalY, out normalZ);
            t = 0f;
            return true;
        }

        var tMin = 0f;
        var tMax = 1f;
        var enterAxis = -1;
        var enterSign = 0f;

        if (!ClipSlab(originX, deltaX, minX, maxX, ref tMin, ref tMax, ref enterAxis, ref enterSign, axis: 0)
            || !ClipSlab(originY, deltaY, minY, maxY, ref tMin, ref tMax, ref enterAxis, ref enterSign, axis: 1)
            || !ClipSlab(originZ, deltaZ, minZ, maxZ, ref tMin, ref tMax, ref enterAxis, ref enterSign, axis: 2))
            return false;

        if (tMin > tMax || tMin < 0f || tMin > 1f)
            return false;

        t = tMin;
        switch (enterAxis)
        {
            case 0:
                normalX = enterSign;
                normalY = 0f;
                normalZ = 0f;
                break;
            case 1:
                normalX = 0f;
                normalY = enterSign;
                normalZ = 0f;
                break;
            case 2:
                normalX = 0f;
                normalY = 0f;
                normalZ = enterSign;
                break;
            default:
                // Degenerate (parallel grazes); default up.
                normalX = 0f;
                normalY = 1f;
                normalZ = 0f;
                break;
        }

        return true;
    }

    private static bool ClipSlab(
        float origin, float delta, float min, float max,
        ref float tMin, ref float tMax,
        ref int enterAxis, ref float enterSign,
        int axis)
    {
        const float eps = 1e-8f;
        if (MathF.Abs(delta) < eps)
        {
            // Parallel: miss if outside the slab.
            if (origin < min || origin > max)
                return false;
            return true;
        }

        var inv = 1f / delta;
        var t0 = (min - origin) * inv;
        var t1 = (max - origin) * inv;
        var sign0 = -1f; // normal of the min face points −axis
        var sign1 = 1f;

        if (t0 > t1)
        {
            (t0, t1) = (t1, t0);
            (sign0, sign1) = (sign1, sign0);
        }

        if (t0 > tMin)
        {
            tMin = t0;
            enterAxis = axis;
            enterSign = sign0;
        }

        if (t1 < tMax)
            tMax = t1;

        return tMin <= tMax;
    }

    private static void NearestFaceNormal(
        float px, float py, float pz,
        float cx, float cy, float cz,
        float hx, float hy, float hz,
        out float nx, out float ny, out float nz)
    {
        var dx = px - cx;
        var dy = py - cy;
        var dz = pz - cz;
        var ax = hx - MathF.Abs(dx);
        var ay = hy - MathF.Abs(dy);
        var az = hz - MathF.Abs(dz);

        if (ax <= ay && ax <= az)
        {
            nx = dx >= 0f ? 1f : -1f;
            ny = 0f;
            nz = 0f;
        }
        else if (ay <= az)
        {
            nx = 0f;
            ny = dy >= 0f ? 1f : -1f;
            nz = 0f;
        }
        else
        {
            nx = 0f;
            ny = 0f;
            nz = dz >= 0f ? 1f : -1f;
        }
    }
}
