namespace AutoCore.Game.Physics.Vehicle;

using AutoCore.Game.Map;

/// <summary>
/// Terrain-only <see cref="IVehicleCollisionQuery"/> for server vehicle wheel casts.
/// Retail used full Havok phantom broadphase; v1 samples a heightfield (map TGA or callback).
/// </summary>
public sealed class TerrainHeightfieldCollisionQuery : IVehicleCollisionQuery
{
    /// <summary>World Y at (x,z). Return false to miss.</summary>
    public delegate bool SampleHeight(float worldX, float worldZ, out float worldY);

    private readonly SampleHeight _sample;
    private readonly float _normalSampleEpsilon;

    /// <summary>Wrap a loaded map heightfield.</summary>
    public TerrainHeightfieldCollisionQuery(MapTerrainHeightfield heightfield, float normalSampleEpsilon = 0.5f)
        : this(RequireField(heightfield), normalSampleEpsilon)
    {
    }

    /// <summary>Delegate-based height sampler (tests and non-TGA sources).</summary>
    public TerrainHeightfieldCollisionQuery(SampleHeight sample, float normalSampleEpsilon = 0.5f)
    {
        _sample = sample ?? throw new ArgumentNullException(nameof(sample));
        _normalSampleEpsilon = normalSampleEpsilon > 0f ? normalSampleEpsilon : 0.5f;
    }

    private static SampleHeight RequireField(MapTerrainHeightfield heightfield)
    {
        if (heightfield == null)
            throw new ArgumentNullException(nameof(heightfield));
        return heightfield.TrySample;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Intersects the ray with a horizontal plane at the sampled terrain Y under the hit XZ.
    /// One refinement sample handles mild non-vertical rays; steep slopes remain approximate.
    /// Direction is treated as a unit vector (same convention as Havok castRay length).
    /// <para>
    /// <b>Penetration:</b> if the cast origin is already below the terrain (and we are casting
    /// generally downward), report a hit at fraction 0 with surface normal — otherwise the
    /// vehicle falls through with no suspension contact (live bug: flying / ground clip).
    /// </para>
    /// </remarks>
    public bool CastRay(
        float originX, float originY, float originZ,
        float dirX, float dirY, float dirZ,
        float maxDistance,
        out VehicleRayHit hit)
    {
        hit = default;
        if (maxDistance <= 0f)
            return false;

        // Height under origin XZ.
        if (!_sample(originX, originZ, out var terrainY))
            return false;

        // Already penetrating (or resting below the plane): force contact at origin.
        // dirY < 0 means casting downward in world (typical suspension cast).
        if (originY <= terrainY && dirY < 0f)
        {
            EstimateNormal(originX, originZ, out var pnx, out var pny, out var pnz);
            hit = new VehicleRayHit(
                fraction: 0f,
                pointX: originX, pointY: terrainY, pointZ: originZ,
                normalX: pnx, normalY: pny, normalZ: pnz,
                isTerrain: true);
            return true;
        }

        if (!TryIntersectHeightPlane(originY, dirY, maxDistance, terrainY, out var fraction))
        {
            // Non-vertical: try height under ray end XZ.
            var endX = originX + dirX * maxDistance;
            var endZ = originZ + dirZ * maxDistance;
            if (!_sample(endX, endZ, out terrainY))
                return false;
            if (!TryIntersectHeightPlane(originY, dirY, maxDistance, terrainY, out fraction))
                return false;
        }

        var dist = fraction * maxDistance;
        var hitX = originX + dirX * dist;
        var hitY = originY + dirY * dist;
        var hitZ = originZ + dirZ * dist;

        // Refine with height at estimated contact XZ (sloped / non-vertical rays).
        if (_sample(hitX, hitZ, out var refinedY))
        {
            if (TryIntersectHeightPlane(originY, dirY, maxDistance, refinedY, out var frac2)
                && frac2 >= 0f && frac2 <= 1f)
            {
                fraction = frac2;
                dist = fraction * maxDistance;
                hitX = originX + dirX * dist;
                hitY = originY + dirY * dist;
                hitZ = originZ + dirZ * dist;
            }
            else
            {
                // Plane at refined height not on segment — keep first hit if still valid.
                if (fraction < 0f || fraction > 1f)
                    return false;
            }
        }

        if (fraction < 0f || fraction > 1f)
            return false;

        EstimateNormal(hitX, hitZ, out var nx, out var ny, out var nz);
        hit = new VehicleRayHit(fraction, hitX, hitY, hitZ, nx, ny, nz, isTerrain: true);
        return true;
    }

    /// <summary>
    /// Ray p(t) = origin + t * dir * maxDistance, t in [0,1], against plane y = terrainY.
    /// Requires dirY != 0; returns false when parallel or intersection outside [0,1].
    /// </summary>
    private static bool TryIntersectHeightPlane(
        float originY, float dirY, float maxDistance, float terrainY, out float fraction)
    {
        fraction = 0f;
        if (dirY == 0f || maxDistance <= 0f)
            return false;

        // originY + (fraction * maxDistance) * dirY = terrainY
        var tAlong = (terrainY - originY) / dirY;
        fraction = tAlong / maxDistance;
        return fraction >= 0f && fraction <= 1f;
    }

    private void EstimateNormal(float x, float z, out float nx, out float ny, out float nz)
    {
        var eps = _normalSampleEpsilon;
        _sample(x - eps, z, out var yL);
        _sample(x + eps, z, out var yR);
        _sample(x, z - eps, out var yB);
        _sample(x, z + eps, out var yF);

        // ∂y/∂x, ∂y/∂z via central differences; Y-up surface normal (−∂y/∂x, 1, −∂y/∂z).
        var dydx = (yR - yL) / (2f * eps);
        var dydz = (yF - yB) / (2f * eps);
        nx = -dydx;
        ny = 1f;
        nz = -dydz;
        var len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len > 1e-8f)
        {
            nx /= len;
            ny /= len;
            nz /= len;
        }
        else
        {
            nx = 0f;
            ny = 1f;
            nz = 0f;
        }
    }
}
