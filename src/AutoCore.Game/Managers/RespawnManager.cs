namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

/// <summary>
/// Handles client RespawnInSector (INC airlift to last repair station).
/// </summary>
public class RespawnManager : Singleton<RespawnManager>
{
    /// <summary>
    /// Optional map resolver for unit tests. When set, used instead of <see cref="MapManager.GetMap"/>.
    /// </summary>
    internal Func<int, SectorMap> ResolveMapForTests { get; set; }

    public void HandleRespawnInSectorPacket(Character character, BinaryReader reader)
    {
        var packet = new RespawnInSectorPacket();
        packet.Read(reader);

        if (!TryRespawnInSector(character, packet.VehicleCoid, out var reason))
            Logger.WriteLog(LogType.Error, $"RespawnInSector failed for character {character?.ObjectId.Coid}: {reason}");
    }

    /// <summary>
    /// Core respawn logic (testable without a live BinaryReader).
    /// </summary>
    public bool TryRespawnInSector(Character character, long clientEntityCoid, out string failureReason)
    {
        failureReason = null;

        if (character == null)
        {
            failureReason = "character is null";
            return false;
        }

        if (character.OwningConnection == null)
        {
            failureReason = "no connection";
            return false;
        }

        var vehicle = character.CurrentVehicle;
        if (vehicle == null)
        {
            failureReason = "no vehicle";
            return false;
        }

        // Client 0xe98 entity COID is the character; SpecialEvent target must match for the airlift path.
        if (clientEntityCoid != 0 &&
            clientEntityCoid != character.ObjectId.Coid &&
            clientEntityCoid != vehicle.ObjectId.Coid)
        {
            Logger.WriteLog(LogType.Debug,
                $"RespawnInSector: unexpected entity COID packet={clientEntityCoid} char={character.ObjectId.Coid} vehicle={vehicle.ObjectId.Coid}");
        }

        if (!TryResolveDestination(character, out var destMap, out var position, out var rotation, out var destReason))
        {
            failureReason = destReason;
            return false;
        }

        var sameMap = character.Map != null && character.Map.ContinentId == destMap.ContinentId;

        if (!sameMap)
        {
            if (!MapManager.Instance.TransferCharacterToMap(character, destMap.ContinentId))
            {
                failureReason = $"map transfer to {destMap.ContinentId} failed";
                return false;
            }

            ApplyPose(character, vehicle, position, rotation);
            RevivePlayer(character, vehicle);

            Logger.WriteLog(LogType.Network,
                $"RespawnInSector: character {character.ObjectId.Coid} transferred to map {destMap.ContinentId} at {position}");
            return true;
        }

        RevivePlayer(character, vehicle);
        ApplyPose(character, vehicle, position, rotation);

        var eventTarget = clientEntityCoid == vehicle.ObjectId.Coid
            ? vehicle.ObjectId
            : character.ObjectId;

        character.OwningConnection.SendGamePacket(new SpecialEventPacket
        {
            Type = SpecialEventType.Respawn,
            Position = position,
            Rotation = rotation,
            Target = eventTarget,
            Flag = 1
        });

        Logger.WriteLog(LogType.Network,
            $"RespawnInSector: character {character.ObjectId.Coid} airlift to {position} on map {destMap.ContinentId} specialTarget={eventTarget.Coid}");
        return true;
    }

    internal bool TryResolveDestination(
        Character character,
        out SectorMap destMap,
        out Vector3 position,
        out Quaternion rotation,
        out string failureReason)
    {
        destMap = null;
        position = default;
        rotation = Quaternion.Default;
        failureReason = null;

        var stationMapId = character.GetLastStationMapId();
        var stationId = character.GetLastStationId();
        var currentMap = character.Map;

        var targetMapId = stationMapId > 0
            ? stationMapId
            : currentMap?.ContinentId ?? character.LastTownId;

        if (targetMapId <= 0 && currentMap == null)
        {
            failureReason = "no map available for respawn";
            return false;
        }

        try
        {
            if (currentMap != null && currentMap.ContinentId == targetMapId)
                destMap = currentMap;
            else if (ResolveMapForTests != null)
                destMap = ResolveMapForTests(targetMapId);
            else
                destMap = MapManager.Instance.GetMap(targetMapId);
        }
        catch (Exception ex)
        {
            if (currentMap != null)
            {
                Logger.WriteLog(LogType.Debug, $"RespawnInSector: station map {targetMapId} failed ({ex.Message}); using current map");
                destMap = currentMap;
            }
            else
            {
                failureReason = $"unable to load map {targetMapId}: {ex.Message}";
                return false;
            }
        }

        if (destMap == null)
        {
            failureReason = "destination map is null";
            return false;
        }

        // Prefer session pose from MarkRepairStation; GenericVar1 is rarely a real object COID.
        if (character.TryGetLastStationPose(out var markedPos, out var markedRot))
        {
            position = markedPos;
            rotation = markedRot;
            return true;
        }

        if (stationId != -1)
        {
            var pad = destMap.GetObjectByCoid(stationId);
            if (pad != null)
            {
                position = pad.Position;
                rotation = pad.Rotation;
                return true;
            }

            Logger.WriteLog(LogType.Debug,
                $"RespawnInSector: no stored pose; station object {stationId} not on map {destMap.ContinentId}; using entry point");
        }

        var entry = destMap.MapData?.EntryPoint ?? default;
        position = entry.ToVector3();
        rotation = Quaternion.Default;
        return true;
    }

    private static void RevivePlayer(Character character, Vehicle vehicle)
    {
        vehicle.Revive();
        character.Revive();
        DirtyHealthAndPosition(character, vehicle);
    }

    private static void ApplyPose(Character character, Vehicle vehicle, Vector3 position, Quaternion rotation)
    {
        character.Position = position;
        character.Rotation = rotation;
        vehicle.Position = position;
        vehicle.Rotation = rotation;
        DirtyHealthAndPosition(character, vehicle);
    }

    private static void DirtyHealthAndPosition(Character character, Vehicle vehicle)
    {
        var mask = GhostObject.PositionMask | GhostObject.HealthMask;
        vehicle.Ghost?.SetMaskBits(mask);
        character.Ghost?.SetMaskBits(mask);
    }
}
