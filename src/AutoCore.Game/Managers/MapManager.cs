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

    /// <summary>
    /// Optional map resolver for unit tests. When set, <see cref="TransferCharacterToMap"/>
    /// uses it instead of <see cref="GetMap"/>.
    /// </summary>
    internal Func<int, SectorMap> ResolveMapForTests { get; set; }

    /// <summary>
    /// When true, map transfer re-scopes ghosts but skips create packets (needs clonebase data).
    /// Production always leaves this false.
    /// </summary>
    internal bool SuppressCreatePacketsForTests { get; set; }

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

    /// <summary>
    /// Re-homes any entity whose Position drifted into a new grid cell since the last tick, across
    /// every loaded map. Called once per sector main-loop tick before ghosting so interest queries
    /// see current positions even when a writer forgot to go through EnterMap/LeaveMap.
    /// </summary>
    public void RebucketAllGrids()
    {
        foreach (var map in SectorMaps.Values)
            map.Grid.RebucketSweep();
    }

    /// <summary>
    /// Advances server-side NPC AI (idle-patrol path following) once per sector tick. Only maps
    /// with live players are ticked — empty continents have no observers to sync poses to.
    /// Called on the sector main loop inside the interface lock, so it never races packet handlers.
    /// </summary>
    /// <param name="nowMs"><see cref="Environment.TickCount64"/> timestamp for this tick.</param>
    /// <param name="deltaSeconds">Elapsed time since the previous tick, in seconds.</param>
    public void TickNpcs(long nowMs, float deltaSeconds)
    {
        foreach (var map in SectorMaps.Values)
        {
            if (map.PlayerCount > 0)
                Npc.NpcTicker.Tick(map, nowMs, deltaSeconds);
        }
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
            if (!MapTransferPreconditions.TryValidate(character, out var failure))
            {
                var detail = failure switch
                {
                    MapTransferPreconditions.Failure.NoConnection
                        => $"TransferCharacterToMap: character {character.ObjectId.Coid} has no connection!",
                    MapTransferPreconditions.Failure.NoVehicle
                        => $"TransferCharacterToMap: character {character.ObjectId.Coid} has no vehicle!",
                    _ => MapTransferPreconditions.Describe(failure)
                };
                Logger.WriteLog(LogType.Error, detail);
                return false;
            }

            var connection = character.OwningConnection;

            var map = ResolveMapForTests != null
                ? ResolveMapForTests(continentId)
                : GetMap(continentId);
            if (map == null)
            {
                Logger.WriteLog(LogType.Error, $"Trying to transfer to non-existant map: {continentId}!");
                return false;
            }

            var mapInfoPacket = new MapInfoPacket();
            map.Fill(mapInfoPacket);

            // Tear down old-map ghosts first so the client does not apply creature updates
            // against objects from the previous sector while MapInfo loads the new one.
            connection.ResetGhosting();

            // Move server-side state onto the destination map before restarting ghosting,
            // so scope queries (PerformScopeQuery) see the new continent's entities.
            character.SetMap(map);
            character.Position = map.MapData.EntryPoint.ToVector3();
            character.Rotation = Quaternion.Default;

            character.CurrentVehicle.SetMap(map);
            character.CurrentVehicle.Position = character.Position;
            character.CurrentVehicle.Rotation = character.Rotation;

            // Keep LastTownId + pose DBData current so logout/relogin resumes on this map.
            character.CaptureWorldStateToDb();

            connection.SendGamePacket(mapInfoPacket, skipOpcode: true);

            // Restart ghosting, re-scope self/vehicle, and re-send create packets.
            // ResetGhosting alone leaves Ghosting/Scoping off permanently until this runs.
            connection.ReestablishGhostingAfterMapTransfer(
                character,
                sendCreatePackets: !SuppressCreatePacketsForTests);

            Logger.WriteLog(LogType.Network,
                $"Transferred character {character.ObjectId.Coid} to map {continentId} and re-established ghosting.");

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
