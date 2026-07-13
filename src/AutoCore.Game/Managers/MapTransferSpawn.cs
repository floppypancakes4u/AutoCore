namespace AutoCore.Game.Managers;

using AutoCore.Game.Constants;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Map;
using AutoCore.Game.Structures;

/// <summary>
/// Resolves spawn pose for continent transfers.
/// Destination maps author <see cref="EnterPointTemplate"/> entries keyed by origin continent
/// (MapTransferType=ContinentObject, MapTransferData=source continent id) — e.g. Back Range EP
/// data=558 for arrivals from Upside. Falls back to the map header EntryPoint.
/// </summary>
public static class MapTransferSpawn
{
    /// <summary>
    /// Returns true when an origin-specific EnterPoint was used; false when EntryPoint fallback.
    /// Always writes a valid pose when <paramref name="destMap"/> is non-null.
    /// </summary>
    public static bool TryResolve(
        SectorMap destMap,
        int sourceContinentId,
        out Vector3 position,
        out Quaternion rotation)
    {
        if (destMap?.MapData == null)
        {
            position = default;
            rotation = Quaternion.Default;
            return false;
        }

        if (sourceContinentId != 0
            && TryFindContinentEnterPoint(destMap, sourceContinentId, out position, out rotation))
        {
            return true;
        }

        var entry = destMap.MapData.EntryPoint;
        position = entry.ToVector3();
        rotation = Quaternion.Default;
        return false;
    }

    private static bool TryFindContinentEnterPoint(
        SectorMap destMap,
        int sourceContinentId,
        out Vector3 position,
        out Quaternion rotation)
    {
        foreach (var template in destMap.MapData.Templates.Values)
        {
            if (template is not EnterPointTemplate ep)
                continue;

            if (ep.MapTransferType != (byte)MapTransferType.ContinentObject)
                continue;

            if (ep.MapTransferData != sourceContinentId)
                continue;

            position = ep.Location.ToVector3();
            rotation = ep.Rotation;
            return true;
        }

        position = default;
        rotation = Quaternion.Default;
        return false;
    }
}
