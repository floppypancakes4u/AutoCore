namespace AutoCore.Game.Physics.Vehicle;

using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Utils;

/// <summary>
/// Per-CBID cache of <see cref="HkVehicleData"/>, same pattern as <c>VehicleGroundMetricsCache</c>.
/// Built after clonebase.wad load; immutable data shared by all instances of that template.
/// </summary>
public static class HkVehicleDataCache
{
    private static readonly Dictionary<int, HkVehicleData> ByCbid = new();

    public static int Count => ByCbid.Count;

    public static void Clear() => ByCbid.Clear();

    public static int BuildFromCloneBases(
        IReadOnlyDictionary<int, CloneBase> cloneBases,
        float gravityY = HkPhysicsConstants.DefaultGravityY,
        float? airDensityOverride = null)
    {
        ByCbid.Clear();
        if (cloneBases == null || cloneBases.Count == 0)
            return 0;

        foreach (var kvp in cloneBases)
        {
            var cv = kvp.Value as CloneBaseVehicle;
            if (cv == null)
                continue;
            try
            {
                ByCbid[kvp.Key] = HkVehicleData.FromVehicleSpecific(
                    cv.VehicleSpecific, kvp.Key, gravityY, airDensityOverride);
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"HkVehicleDataCache: skip cbid={kvp.Key}: {ex.Message}");
            }
        }

        Logger.WriteLog(LogType.Initialize, $"HkVehicleDataCache: {ByCbid.Count} vehicle physics templates");
        return ByCbid.Count;
    }

    public static bool TryGet(int cbid, out HkVehicleData data)
        => ByCbid.TryGetValue(cbid, out data);

    public static HkVehicleData GetOrCompute(
        int cbid,
        VehicleSpecific vs,
        float gravityY = HkPhysicsConstants.DefaultGravityY,
        float? airDensityOverride = null)
    {
        if (ByCbid.TryGetValue(cbid, out var cached))
            return cached;
        var data = HkVehicleData.FromVehicleSpecific(vs, cbid, gravityY, airDensityOverride);
        if (cbid != 0)
            ByCbid[cbid] = data;
        return data;
    }
}
