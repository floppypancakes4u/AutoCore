namespace AutoCore.Game.Managers;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Utils;
using AutoCore.Utils.Memory;

public class MapManager : Singleton<MapManager>
{
    private Dictionary<int, SectorMap> SectorMaps { get; } = new();

    public bool Initialize()
    {
        var continentObjects = AssetManager.Instance.GetContinentObjects().ToList();
        
        if (continentObjects.Count == 0)
        {
            Logger.WriteLog(LogType.Error, "No continent objects available to load maps. Continuing with no maps loaded.");
            return true;
        }

        var loadedCount = 0;
        var failedCount = 0;
        
        foreach (var continentObject in continentObjects) // TODO: only load IsPersistent maps (the others are instanceable?)
        {
            try
            {
                // TODO: preload only persistent maps?
                SetupMap(continentObject.Id);
                loadedCount++;
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error, $"Failed to setup map {continentObject.Id}: {ex.Message}");
                failedCount++;
            }
        }

        if (loadedCount > 0)
        {
            Logger.WriteLog(LogType.Initialize, $"MapManager initialized with {loadedCount} maps" + (failedCount > 0 ? $" ({failedCount} failed)" : "") + ".");
        }
        else if (failedCount > 0)
        {
            Logger.WriteLog(LogType.Error, $"MapManager failed to load any maps ({failedCount} failed). Continuing anyway.");
        }

        return true;
    }

    private void SetupMap(int continentId)
    {
        if (SectorMaps.ContainsKey(continentId))
            throw new Exception($"Map {continentId} is already setup!");

        SectorMaps[continentId] = new SectorMap(continentId);
    }

    private bool TrySetupMap(int continentId, out string error)
    {
        error = null;

        if (SectorMaps.ContainsKey(continentId))
            return true; // Already loaded

        // Check if the continent object exists in the loaded (filtered) database
        var continentObject = AssetManager.Instance.GetContinentObject(continentId);
        if (continentObject == null)
        {
            // Try to look up from wad.xml for a better error message
            var wadContinent = AssetManager.Instance.GetContinentObjectFromWad(continentId);
            if (wadContinent != null)
            {
                var mapFileName = $"{wadContinent.MapFileName}.fam";
                error = $"Map '{wadContinent.DisplayName}' (continent {continentId}) cannot be loaded - map file '{mapFileName}' not found in GLM archives";
            }
            else
            {
                error = $"Continent object {continentId} not found in database";
            }
            return false;
        }

        // Check if the map file exists
        var famFileName = $"{continentObject.MapFileName}.fam";
        if (!AssetManager.Instance.HasFileInGLMs(famFileName))
        {
            error = $"Map file '{famFileName}' not found in GLM archives for continent {continentId} ({continentObject.DisplayName})";
            return false;
        }

        try
        {
            SectorMaps[continentId] = new SectorMap(continentId);
            Logger.WriteLog(LogType.Initialize, $"MapManager: Dynamically loaded map {continentId} ({continentObject.DisplayName})");
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load map {continentId}: {ex.Message}";
            return false;
        }
    }

    public SectorMap GetMap(int continentId)
    {
        if (SectorMaps.TryGetValue(continentId, out var sectorMap))
            return sectorMap;

        // Try to load the map dynamically
        if (TrySetupMap(continentId, out var error))
        {
            return SectorMaps[continentId];
        }

        throw new Exception($"Unknown map ({continentId}) requested! {error}");
    }

    public bool TransferCharacterToMap(Character character, int continentId)
    {
        try
        {
            var map = GetMap(continentId);
            if (map == null)
            {
                Logger.WriteLog(LogType.Error, $"Trying to transfer to non-existant map: {continentId}!");
                return false;
            }

            var mapInfoPacket = new MapInfoPacket();
            map.Fill(mapInfoPacket);

            character.OwningConnection.ResetGhosting();
            character.OwningConnection.SendGamePacket(mapInfoPacket, skipOpcode: true);

            character.SetMap(map);
            character.Position = map.MapData.EntryPoint.ToVector3();
            character.Rotation = Quaternion.Default;

            character.CurrentVehicle.SetMap(map);
            character.CurrentVehicle.Position = character.Position;
            character.CurrentVehicle.Rotation = character.Rotation;

            return true;
        }
        catch (Exception ex)
        {
            Logger.WriteLog(LogType.Error, $"Failed to transfer character to map {continentId}: {ex.Message}");
            return false;
        }
    }

    public void HandleTransferRequestPacket(Character character, BinaryReader reader)
    {
        var packet = new MapTransferRequestPacket();
        packet.Read(reader);

        if (packet.Type != MapTransferType.ContinentObject)
        {
            Logger.WriteLog(LogType.Error, $"Not implemented map transfer type: {packet.Type}!");
            return;
        }

        TransferCharacterToMap(character, packet.Data);
    }

    public void HandleChangeCombatModeRequest(Character character, BinaryReader reader)
    {
        var packet = new ChangeCombatModeRequestPacket();
        packet.Read(reader);

        // TODO: Update the Character

        // Always send true as success, false isn't implemented correctly and the client doesn't update, keeping the previous values, but updates the UI
        var response = new ChangeCombatModeResponsePacket
        {
            CharacterCoid = packet.CharacterCoid,
            Mode = packet.Mode,
            Success = true
        };

        character.OwningConnection.SendGamePacket(response);
    }
}
