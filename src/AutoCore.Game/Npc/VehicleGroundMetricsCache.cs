namespace AutoCore.Game.Npc;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Structures;
using AutoCore.Utils;

/// <summary>
/// Per-vehicle-template ground metrics built once after clonebase.wad load.
/// Ride height places the chassis origin so wheel bottoms sit on the terrain sample:
/// <c>chassisY ≈ terrainY + meanRadius − meanHardpointY</c> (hardpoint ≈ wheel center local Y).
/// </summary>
public static class VehicleGroundMetricsCache
{
    private static readonly Dictionary<int, VehicleGroundMetrics> ByCbid = new();

    /// <summary>
    /// Scale applied to computed ride height. 1 = full formula; lower if still high live.
    /// </summary>
    public static float RideHeightScale { get; set; } = 1f;

    /// <summary>Clamp for chassis height above terrain (world units).</summary>
    public static float MaxRideHeight { get; set; } = 1.25f;

    public static int Count => ByCbid.Count;

    /// <summary>Average chassis ride height over templates with usable wheel radii (last Build).</summary>
    public static float LastAverageRideHeight { get; private set; }

    /// <summary>Count of templates contributing to <see cref="LastAverageRideHeight"/>.</summary>
    public static int LastWithRadiusCount { get; private set; }

    public static void Clear()
    {
        ByCbid.Clear();
        LastAverageRideHeight = 0f;
        LastWithRadiusCount = 0;
    }

    /// <summary>Scan all clonebases; call once after WAD load.</summary>
    public static int BuildFromCloneBases(IReadOnlyDictionary<int, CloneBase> cloneBases)
    {
        ByCbid.Clear();
        LastAverageRideHeight = 0f;
        LastWithRadiusCount = 0;
        if (cloneBases == null || cloneBases.Count == 0)
            return 0;

        var withRadius = 0;
        var sumRide = 0f;
        foreach (var kvp in cloneBases)
        {
            // Classic type check (Stryker-safe vs pattern-match discard).
            var cv = kvp.Value as CloneBaseVehicle;
            if (cv == null)
                continue;

            var metrics = Compute(cv.VehicleSpecific);
            ByCbid[kvp.Key] = metrics;
            // Stryker disable once equality : radius usability threshold for averages
            if (metrics.MeanWheelRadius > 0.05f)
            {
                withRadius++;
                sumRide += metrics.ChassisHeightAboveTerrain;
            }
        }

        LastWithRadiusCount = withRadius;
        LastAverageRideHeight = withRadius > 0 ? sumRide / withRadius : 0f;
        Logger.WriteLog(LogType.Initialize,
            $"VehicleGroundMetricsCache: {ByCbid.Count} vehicle templates, " +
            $"{withRadius} with wheel radii, avg ride height={LastAverageRideHeight:F3} (scale={RideHeightScale})");
        return ByCbid.Count;
    }

    public static bool TryGet(int cbid, out VehicleGroundMetrics metrics)
        => ByCbid.TryGetValue(cbid, out metrics);

    /// <summary>
    /// Clearance to add to terrain Y for this template (already scaled/clamped).
    /// </summary>
    public static float GetRideHeight(int cbid)
    {
        if (!ByCbid.TryGetValue(cbid, out var m))
            return TerrainContactPlane.DefaultGroundClearance;
        return m.ChassisHeightAboveTerrain;
    }

    /// <summary>Live recompute for tests / vehicles without cache.</summary>
    public static VehicleGroundMetrics Compute(VehicleSpecific vs)
    {
        var radiusSum = 0f;
        var radiusCount = 0;
        if (vs.WheelRadius != null)
        {
            foreach (var r in vs.WheelRadius)
            {
                if (r > 0.05f && r < 5f)
                {
                    radiusSum += r;
                    radiusCount++;
                }
            }
        }

        var meanR = radiusCount > 0 ? radiusSum / radiusCount : 0f;

        var hpYSum = 0f;
        var hpCount = 0;
        var maxAbsZ = 0f;
        var maxAbsX = 0f;
        if (vs.WheelHardPoints != null)
        {
            foreach (var hp in vs.WheelHardPoints)
            {
                // Stryker disable once equality : zero hardpoint epsilon
                // Stryker disable once logical : all-near-zero skip
                if (MathF.Abs(hp.X) < 1e-4f && MathF.Abs(hp.Z) < 1e-4f && MathF.Abs(hp.Y) < 1e-4f)
                    continue;
                hpYSum += hp.Y;
                hpCount++;
                maxAbsZ = Math.Max(maxAbsZ, MathF.Abs(hp.Z));
                maxAbsX = Math.Max(maxAbsX, MathF.Abs(hp.X));
            }
        }

        var meanHpY = hpCount > 0 ? hpYSum / hpCount : 0f;

        // Wheel bottom local Y ≈ hardpoint.Y − radius → chassisY + that = terrain
        // chassisY = terrain + radius − hardpoint.Y
        var raw = meanR - meanHpY;
        // Stryker disable once equality : clamp-at-zero boundary
        if (raw < 0f)
            raw = 0f;
        if (!float.IsFinite(raw))
            raw = 0f;

        var ride = Math.Clamp(raw * RideHeightScale, 0f, MaxRideHeight);

        return new VehicleGroundMetrics(
            meanR,
            meanHpY,
            ride,
            // Stryker disable once equality : footprint threshold
            maxAbsZ > 0.5f ? Math.Clamp(maxAbsZ, 1.5f, 8f) : TerrainContactPlane.DefaultHalfLength,
            // Stryker disable once equality : footprint threshold
            maxAbsX > 0.3f ? Math.Clamp(maxAbsX, 0.8f, 4f) : TerrainContactPlane.DefaultHalfWidth,
            radiusCount,
            hpCount);
    }
}

/// <summary>Cached ground metrics for one vehicle clonebase.</summary>
public readonly struct VehicleGroundMetrics
{
    public VehicleGroundMetrics(
        float meanWheelRadius,
        float meanHardpointY,
        float chassisHeightAboveTerrain,
        float halfLength,
        float halfWidth,
        int wheelRadiusCount,
        int hardpointCount)
    {
        MeanWheelRadius = meanWheelRadius;
        MeanHardpointY = meanHardpointY;
        ChassisHeightAboveTerrain = chassisHeightAboveTerrain;
        HalfLength = halfLength;
        HalfWidth = halfWidth;
        WheelRadiusCount = wheelRadiusCount;
        HardpointCount = hardpointCount;
    }

    public float MeanWheelRadius { get; }
    public float MeanHardpointY { get; }
    /// <summary>Add to heightfield Y when packing chassis origin.</summary>
    public float ChassisHeightAboveTerrain { get; }
    public float HalfLength { get; }
    public float HalfWidth { get; }
    public int WheelRadiusCount { get; }
    public int HardpointCount { get; }
}
