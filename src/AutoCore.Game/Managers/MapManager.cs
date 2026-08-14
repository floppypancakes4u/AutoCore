namespace AutoCore.Game.Managers;

using System.Diagnostics.CodeAnalysis;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;
using AutoCore.Utils.Logging;
using AutoCore.Utils.Memory;
using AutoCore.Utils.Reliability;

public class MapManager : Singleton<MapManager>
{
    private Dictionary<int, SectorMap> SectorMaps { get; } = new();

    /// <summary>
    /// Private per-player copies of instanced continents (see <see cref="InstancedContinents"/>),
    /// keyed by (continentId, owning character coid). Shared maps stay in <see cref="SectorMaps"/>.
    /// </summary>
    private Dictionary<(int ContinentId, long OwnerCoid), SectorMap> InstanceMaps { get; } = new();

    /// <summary>
    /// Flat snapshot of shared + instance maps for the per-tick loops. Rebuilt only on
    /// register/unregister so hot loops allocate nothing.
    /// </summary>
    private SectorMap[] _allMapsCache = Array.Empty<SectorMap>();

    internal SectorMap[] AllMaps() => _allMapsCache;

    /// <summary>Test seam: snapshot of every registered map (shared + instances).</summary>
    internal SectorMap[] AllMapsForTests() => AllMaps();

    private void RebuildAllMapsCache()
        => _allMapsCache = SectorMaps.Values.Concat(InstanceMaps.Values).ToArray();

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

    /// <summary>Test seam: inject a pre-built <see cref="SectorMap"/> without GLM/fam load.</summary>
    internal void RegisterMapForTests(SectorMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        SectorMaps[map.ContinentId] = map;
        RebuildAllMapsCache();
    }

    /// <summary>Test seam: register a pre-built instance map without GLM/fam load.</summary>
    internal void RegisterInstanceForTests(SectorMap map, long ownerCoid)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.MarkAsInstance(ownerCoid);
        InstanceMaps[(map.ContinentId, ownerCoid)] = map;
        RebuildAllMapsCache();
    }

    /// <summary>
    /// Test seam: substitutes <c>new SectorMap(continentId)</c> in <see cref="GetMapForCharacter"/>
    /// so instance-registry logic runs without GLM/fam asset I/O.
    /// </summary>
    internal Func<int, SectorMap> CreateInstanceForTests { get; set; }

    /// <summary>Test seam: drop all registered maps (including ones injected by tests).</summary>
    internal void ClearMapsForTests()
    {
        SectorMaps.Clear();
        InstanceMaps.Clear();
        RebuildAllMapsCache();
    }

    /// <summary>
    /// Loads all continent maps via live <see cref="SetupMap"/>. Empty-catalog soft-fail is
    /// unit-tested; per-map load uses excluded SetupMap asset I/O.
    /// </summary>
    [ExcludeFromCodeCoverage(Justification = "Live map bootstrap loop via SetupMap; empty-catalog soft-fail unit-tested.")]
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
                Logger.WriteException(LogType.Error, $"Failed to setup map {continentObject.Id}", ex);
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
        // SS-12: isolate per map so one bad grid cannot skip re-bucketing for every other map,
        // which would leave interest queries reading stale positions server-wide.
        Guard.ForEach(
            AllMaps(),
            "grid rebucket sweep",
            map => map.Grid.RebucketSweep(),
            describe: map => $"map {map.ContinentId}#{map.InstanceSerial}");
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
        // SS-12: isolate per map. NpcTicker already isolates individual NPCs, but a failure in
        // map-level setup must not stop the remaining maps from ticking their AI.
        Guard.ForEach(
            AllMaps(),
            "NPC map tick",
            map =>
            {
                if (map.PlayerCount > 0)
                    Npc.NpcTicker.Tick(map, nowMs, deltaSeconds);
            },
            describe: map => $"map {map.ContinentId}#{map.InstanceSerial}");
    }

    /// <summary>
    /// Force <see cref="GhostObject.PositionMask"/> dirty on every pathing NPC vehicle that has
    /// an observer map. Live diagnosis: even with keep-dirty + rate floor, Gunny only shipped
    /// ~4 GhostPacks then silence — dirty list was going cold. This is the hard guarantee that
    /// path vehicles re-enter the TNL non-zero update queue every sector tick.
    /// </summary>
    /// <returns>Number of vehicles force-dirtied this call.</returns>
    /// <summary>
    /// Force-dirty only path vehicles that are currently ghosted to at least one connection.
    /// Dirties on unghosted shells are no-ops for packing (CollapseDirtyList finds no GhostInfo).
    /// </summary>
    public int ForcePathVehiclePoseDirty()
    {
        var n = 0;
        foreach (var map in AllMaps())
        {
            if (map.PlayerCount <= 0)
                continue;

            foreach (var entity in map.NpcAiEntities)
            {
                if (entity is not Vehicle vehicle)
                    continue;
                if (vehicle.CoidCurrentPath <= 0 || vehicle.Ghost == null)
                    continue;
                if (vehicle.IsCorpse)
                    continue;
                // No connection has this ghost in scope → SetMaskBits cannot enqueue a pack.
                if (vehicle.Ghost.GetFirstObjectRef() == null)
                    continue;

                vehicle.Ghost.SetMaskBits(GhostObject.PositionMask);
                n++;
            }
        }

        return n;
    }

    /// <summary>Live SectorMap(continentId) bootstrap from AssetManager map data / GLM.</summary>
    [ExcludeFromCodeCoverage(Justification = "Live map asset I/O via SectorMap(int); tests use RegisterMapForTests/CreateForTests.")]
    private void SetupMap(int continentId)
    {
        if (SectorMaps.ContainsKey(continentId))
            throw new Exception($"Map {continentId} is already setup!");

        SectorMaps[continentId] = new SectorMap(continentId);
        RebuildAllMapsCache();
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
            RebuildAllMapsCache();
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

    /// <summary>
    /// Resolves the map a character should enter. Shared continents return the one shared
    /// <see cref="SectorMap"/> (identical to <see cref="GetMap"/>). Instanced continents
    /// (<see cref="InstancedContinents"/>) ALWAYS create a fresh private copy — retail relog
    /// policy; persisted character state replays into it via PerPlayerLoad +
    /// ApplyMissionPhaseWorldState — except when the character is still live on their current
    /// instance (same-continent warp/transfer), which is reused rather than torn down.
    /// </summary>
    public SectorMap GetMapForCharacter(int continentId, Character character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (!InstancedContinents.IsInstanced(continentId))
            return GetMap(continentId);

        var ownerCoid = character.ObjectId.Coid;
        var key = (continentId, ownerCoid);

        if (InstanceMaps.TryGetValue(key, out var existing))
        {
            // Same-continent re-entry (/warp, TransferMap reaction to the current continent):
            // the character is still on the live instance — never tear down an occupied map.
            if (existing.Players.Contains(character))
                return existing;

            // Disposal is synchronous in LeaveMap, so a leftover entry indicates a prior fault.
            Logger.WriteLog(LogType.Error,
                "MapManager: stale instance {0} of continent {1} (owner {2}) found on entry — disposing before fresh create",
                existing.InstanceSerial,
                continentId,
                ownerCoid);
            DisposeInstance(existing, ownerCoid);
        }

        var instance = CreateInstanceForTests != null
            ? CreateInstanceForTests(continentId)
            : CreateInstanceLive(continentId);
        instance.MarkAsInstance(ownerCoid);
        InstanceMaps[key] = instance;
        RebuildAllMapsCache();

        Logger.WriteLog(LogType.Debug,
            "MapManager: created instance {0} of continent {1} (owner {2})",
            instance.InstanceSerial,
            continentId,
            ownerCoid);
        return instance;
    }

    /// <summary>Live per-player instance bootstrap from AssetManager map data / GLM.</summary>
    [ExcludeFromCodeCoverage(Justification = "Live map asset I/O via SectorMap(int); tests use CreateInstanceForTests.")]
    private static SectorMap CreateInstanceLive(int continentId) => new(continentId);

    /// <summary>
    /// Unregisters and tears down a per-player instance. Registry removal is
    /// ReferenceEquals-guarded so a late dispose of an old copy can never evict a fresh
    /// re-registration under the same key. Unregisters FIRST so a teardown fault can never
    /// leak a dead map into the tick loops (SS-30); per-entity faults are contained inside
    /// <see cref="SectorMap.TearDownLocalEntities"/>.
    /// </summary>
    public void DisposeInstance(SectorMap map, long ownerCoid)
    {
        ArgumentNullException.ThrowIfNull(map);

        var key = (map.ContinentId, ownerCoid);
        if (InstanceMaps.TryGetValue(key, out var stored) && ReferenceEquals(stored, map))
        {
            InstanceMaps.Remove(key);
            RebuildAllMapsCache();
        }

        Guard.Run(
            $"instance teardown (continent {map.ContinentId}#{map.InstanceSerial})",
            map.TearDownLocalEntities);

        // Purge cross-cutting singleton state keyed on this instance's serial/identity.
        Guard.Run(
            $"instance latch purge (continent {map.ContinentId}#{map.InstanceSerial})",
            () =>
            {
                TriggerManager.Instance.ClearInstance(map.InstanceSerial);
                Combat.MapPropCorpseDespawn.CancelForMap(map);
                Combat.VehicleMapPropRam.ClearForInstance(map.InstanceSerial);
            });

        Logger.WriteLog(LogType.Debug,
            "MapManager: instance {0} of continent {1} disposed (owner {2})",
            map.InstanceSerial,
            map.ContinentId,
            ownerCoid);
    }

    public bool TransferCharacterToMap(Character character, int continentId)
    {
        // Operation scope only — control flow (early returns, catch-all) is unchanged.
        var transferOperation = GameLog.Operation("MapTransfer",
            ("CharacterId", character?.ObjectId.Coid),
            ("FromMapId", character?.Map?.ContinentId),
            ("ToMapId", continentId));

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
                transferOperation.Fail(null, ("Reason", failure.ToString()));
                return false;
            }

            var map = ResolveMapForTests != null
                ? ResolveMapForTests(continentId)
                : GetMapForCharacter(continentId, character);
            if (map == null)
            {
                Logger.WriteLog(LogType.Error, $"Trying to transfer to non-existant map: {continentId}!");
                transferOperation.Fail(null, ("Reason", "UnknownMap"));
                return false;
            }

            // Spawn at the EnterPoint keyed by origin continent when present (Upside gate on
            // Back Range, etc.); otherwise the map header EntryPoint.
            var sourceContinentId = character.Map?.ContinentId ?? 0;
            MapTransferSpawn.TryResolve(map, sourceContinentId, out var spawnPos, out var spawnRot);
            return TransferCharacterToMapCore(character, map, spawnPos, spawnRot, transferOperation);
        }
        catch (Exception ex)
        {
            transferOperation.Fail(ex);
            Logger.WriteException(LogType.Error, $"Failed to transfer character to map {continentId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Transfer onto a specific live <see cref="SectorMap"/> instance at an explicit pose.
    /// Used by GM /portto and /porttome so the mover joins the anchor player's map copy
    /// (including per-player instances) rather than minting a fresh continent via
    /// <see cref="GetMapForCharacter"/>.
    /// </summary>
    public bool TransferCharacterToMap(Character character, SectorMap map, Vector3 spawnPos, Quaternion spawnRot)
    {
        var continentId = map?.ContinentId ?? 0;
        var transferOperation = GameLog.Operation("MapTransfer",
            ("CharacterId", character?.ObjectId.Coid),
            ("FromMapId", character?.Map?.ContinentId),
            ("ToMapId", continentId));

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
                transferOperation.Fail(null, ("Reason", failure.ToString()));
                return false;
            }

            if (map == null)
            {
                Logger.WriteLog(LogType.Error, "TransferCharacterToMap: destination map is null!");
                transferOperation.Fail(null, ("Reason", "UnknownMap"));
                return false;
            }

            return TransferCharacterToMapCore(character, map, spawnPos, spawnRot, transferOperation);
        }
        catch (Exception ex)
        {
            transferOperation.Fail(ex);
            Logger.WriteException(LogType.Error, $"Failed to transfer character to map {continentId}", ex);
            return false;
        }
    }

    /// <summary>
    /// Shared body after preconditions + destination map are known. Completes or fails
    /// <paramref name="transferOperation"/>; caller owns the try/catch for unexpected faults
    /// only when it does not already wrap this call (core itself is fault-contained).
    /// </summary>
    private bool TransferCharacterToMapCore(
        Character character,
        SectorMap map,
        Vector3 spawnPos,
        Quaternion spawnRot,
        OperationScope transferOperation)
    {
        var continentId = map.ContinentId;
        var connection = character.OwningConnection;
        var sourceContinentId = character.Map?.ContinentId ?? 0;

        var mapInfoPacket = new MapInfoPacket();
        map.Fill(mapInfoPacket);

        // Tear down old-map ghosts first so the client does not apply creature updates
        // against objects from the previous sector while MapInfo loads the new one.
        connection.ResetGhosting();

        // SS-51: close the world-entry gate before the character lands on the new map. EnterMap
        // runs ApplyMissionPhaseWorldState on SetMap, which would otherwise fire mission gates at
        // a client still loading the destination FAM. CompleteWorldEntry runs when Stage3 ack
        // releases SendLocalPlayerCreatePackets.
        character.BeginWorldEntry();

        // Move server-side state onto the destination map before restarting ghosting,
        // so scope queries (PerformScopeQuery) see the new continent's entities.
        character.SetMap(map);
        character.Position = spawnPos;
        character.Rotation = spawnRot;

        character.CurrentVehicle.SetMap(map);
        // Map transfer teleport is discontinuous — drop stale physics so next Apply recreates.
        character.CurrentVehicle.ClearPhysicsInstance();
        character.CurrentVehicle.SetPosition(spawnPos);
        character.CurrentVehicle.Rotation = spawnRot;

        // Keep LastTownId + pose DBData current so logout/relogin resumes on this map.
        character.CaptureWorldStateToDb();

        // Old-map foreign CreateVehicle holds must die with ResetGhosting even though
        // destination Creates wait for the client's Stage2/Stage3 handshake.
        connection.ClearGlobalVehicleCreateTracking();
        connection.BeginPendingMapTransferHandshake(character, continentId, sourceContinentId);
        connection.SendGamePacket(mapInfoPacket, skipOpcode: true);

        Logger.WriteLog(LogType.Network,
            $"Transferred character {character.ObjectId.Coid} to map {continentId}; waiting for Stage2.");

        transferOperation.Complete();
        return true;
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
