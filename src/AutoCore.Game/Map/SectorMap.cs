namespace AutoCore.Game.Map;

using System.Linq;
using global::TNL.Entities;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.TNL.Ghost;
using AutoCore.Utils;

public class SectorMap
{
    /// <summary>
    /// Isolation lever: when false, foreign global vehicles are neither CreateVehicle'd nor ObjectInScope'd.
    /// Local player vehicle is unaffected.
    /// </summary>
    public static bool ScopeGlobalVehicles { get; set; } = true;

    /// <summary>Isolation lever: when false, skip CreateVehicle for foreign global vehicles (still may ghost).</summary>
    public static bool ScopeGlobalVehicleCreate { get; set; } = true;

    /// <summary>
    /// Send CreateVehicle but skip ObjectInScope for foreign globals. Disabled by default because
    /// retail client's GhostVehicle initial path can crash while rendering nearby NPC vehicles.
    /// </summary>
    public static bool ScopeGlobalVehicleGhost { get; set; } = false;

    /// <summary>Isolation lever: when false, skip sending GroupReactionCall (0x206C) after reactions run.</summary>
    public static bool SendGroupReactionCall { get; set; } = true;

    public int ContinentId { get; }
    public long LocalCoidCounter { get; set; }
    public MapData MapData { get; private set; }
    public ContinentObject ContinentObject => MapData.ContinentObject;
    public Dictionary<TFID, ClonedObjectBase> Objects { get; } = new();
    public Dictionary<TFID, Trigger> Triggers { get; } = new();
    public Dictionary<TFID, Reaction> Reactions { get; } = new();

    /// <summary>NPC creatures/vehicles currently on this map with an active <see cref="Npc.NpcAiState"/>.</summary>
    public List<ClonedObjectBase> NpcAiEntities { get; } = new();

    /// <summary>Live <see cref="Character"/> count on this map, maintained by EnterMap/LeaveMap.</summary>
    public int PlayerCount { get; private set; }

    /// <summary>Live players on this map, maintained by EnterMap/LeaveMap. Tier-1 scope: always ghosted.</summary>
    public List<Character> Players { get; } = new();

    /// <summary>XZ spatial index of the map's entities, maintained by EnterMap/LeaveMap.</summary>
    public SpatialHashGrid Grid { get; private set; } = new();

    public SectorMap(int continentId)
    {
        ContinentId = continentId;

        MapData = AssetManager.Instance.GetMapData(ContinentId);
        LocalCoidCounter = MapData.HighestCoid + 1;

        InitializeLocalObjects();
    }

    /// <summary>
    /// Builds a minimal in-memory map for unit tests (no GLM/WAD I/O).
    /// </summary>
    internal static SectorMap CreateForTests(ContinentObject continentObject, Vector4 entryPoint)
    {
        ArgumentNullException.ThrowIfNull(continentObject);

        var map = (SectorMap)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SectorMap));
        var mapData = new MapData(continentObject);
        typeof(MapData).GetProperty(nameof(MapData.EntryPoint))!
            .SetValue(mapData, entryPoint);

        typeof(SectorMap).GetField($"<{nameof(ContinentId)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, continentObject.Id);
        typeof(SectorMap).GetField($"<{nameof(MapData)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, mapData);
        typeof(SectorMap).GetField($"<{nameof(Objects)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, ClonedObjectBase>());
        typeof(SectorMap).GetField($"<{nameof(Triggers)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, Trigger>());
        typeof(SectorMap).GetField($"<{nameof(Reactions)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new Dictionary<TFID, Reaction>());
        typeof(SectorMap).GetField($"<{nameof(NpcAiEntities)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new List<ClonedObjectBase>());
        typeof(SectorMap).GetField($"<{nameof(Players)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new List<Character>());
        typeof(SectorMap).GetField($"<{nameof(Grid)}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(map, new SpatialHashGrid());

        map.LocalCoidCounter = mapData.HighestCoid + 1;
        return map;
    }

    private void InitializeLocalObjects()
    {
        // TODO: some objects are only needed on Sector? Global? Both?

        foreach (var template in MapData.Templates)
        {
            var obj = template.Value.Create();
            if (obj == null)
                continue;

            obj.LoadCloneBase(template.Value.CBID);
            obj.SetCoid(template.Value.COID, false);
            obj.Faction = template.Value.Faction;
            obj.Layer = template.Value.Layer;
            obj.SetMap(this);

            // Do NOT CreateGhost for all GraphicsObjects here — flooding the ghost table
            // with every map prop exhausts client ghost slots and NPCs stop appearing.
            // Combat props get ghosts lazily via MakeNotInvincible / first TakeDamage.

            if (obj is SpawnPoint sp)
            {
                sp.Spawn();
            }
        }
    }

    public ClonedObjectBase GetLocalObject(long coid)
    {
        if (Objects.TryGetValue(new TFID(coid, false), out var value))
            return value;

        return null;
    }

    public ClonedObjectBase GetObject(long coid)
    {
        // Try local first, then global
        if (Objects.TryGetValue(new TFID(coid, false), out var localValue))
            return localValue;

        if (Objects.TryGetValue(new TFID(coid, true), out var globalValue))
            return globalValue;

        return null;
    }

    public ClonedObjectBase GetObjectByCoid(long coid)
    {
        // Search by COID only, ignoring the Global flag
        foreach (var kvp in Objects)
        {
            if (kvp.Key.Coid == coid)
                return kvp.Value;
        }

        return null;
    }

    public void Fill(MapInfoPacket packet)
    {
        packet.RegionId = -1;
        packet.RegionType = TilesetType.Universal;
        packet.RegionLevel = 1;
        packet.LayerId = 0; // TODO: CVOGCharacter::ChooseLayer
        packet.ObjectiveIndex = ContinentObject.Objective;
        packet.MapName = $"{ContinentObject.MapFileName}.fam";
        packet.IsTown = ContinentObject.IsTown;
        packet.IsArena = ContinentObject.IsArena;
        packet.OwningFaction = ContinentObject.OwningFaction;
        packet.ContinentObjectId = ContinentId;
        packet.IsPersistent = ContinentObject.IsPersistent;
        packet.MapIterationVersion = MapData.IterationVersion;
        packet.ContestedMissionId = ContinentObject.ContestedMission;
        packet.Coid = ContinentId;
        packet.TemporalRandomSeed = 123456789;
        packet.CoidMap = ContinentId;
        packet.NumModulePlacements = 0;
        packet.PositionX = 0.0f;
        packet.PositionY = 0.0f;
        packet.PositionZ = 0.0f;
        packet.WeatherUpdateSize = 0;
    }

    /// <summary>
    /// Resolves a map path (wad.xml/.fam <c>MapPathTemplate</c>, ObjectTemplate type 70) from the
    /// header <see cref="MapData.Templates"/> table by COID, e.g. for
    /// <see cref="SpawnPointTemplate.MapPathCoid"/>.
    /// </summary>
    public bool TryGetMapPath(long coid, out MapPathTemplate mapPath)
    {
        mapPath = null;
        if (coid <= 0)
            return false;

        if (MapData.Templates.TryGetValue(coid, out var template) && template is MapPathTemplate found)
        {
            mapPath = found;
            return true;
        }

        return false;
    }

    public void EnterMap(ClonedObjectBase clonedObject)
    {
        if (clonedObject is Trigger trigger)
            Triggers.Add(trigger.ObjectId, trigger);

        if (clonedObject is Reaction reaction)
            Reactions.Add(reaction.ObjectId, reaction);

        if (clonedObject is Character character)
        {
            PlayerCount++;
            Players.Add(character);
        }

        if (HasNpcAi(clonedObject))
            NpcAiEntities.Add(clonedObject);

        Grid.Add(clonedObject);

        if (Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is already on the map!");

        Objects.Add(clonedObject.ObjectId, clonedObject);
    }

    /// <summary>True when a Creature/Vehicle carries a live server-side AI state (NPC.md).</summary>
    private static bool HasNpcAi(ClonedObjectBase clonedObject)
    {
        return clonedObject switch
        {
            Creature creature => creature.NpcAi != null,
            Vehicle vehicle => vehicle.NpcAi != null,
            _ => false
        };
    }

    /// <summary>
    /// Resolve the map header <see cref="MapData.PerPlayerLoadTrigger"/> if the trigger exists
    /// as a live entity on this sector. Mirrors client FUN_004CDCC0 → FUN_004BB1C0:
    /// only fire when the COID is findable in the world object hash.
    /// COID &lt; 0 means disabled (e.g. biomek 708 has no PerPlayerLoad).
    /// </summary>
    public bool TryGetPerPlayerLoadTrigger(out Trigger trigger)
    {
        trigger = null;
        var trigCoid = MapData.PerPlayerLoadTrigger;
        if (trigCoid < 0)
            return false;

        if (GetObjectByCoid(trigCoid) is Trigger live
            && live.Template is not null
            && live.Template.Reactions.Count > 0)
        {
            trigger = live;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retail-aligned on-load grant: if PerPlayerLoad trigger is findable, fire its reaction list
    /// (e.g. 707: 16217 → GiveMission 14137 → mission 554). Server tracks via
    /// <see cref="Reaction"/> handlers and notifies client with 0x206C.
    /// Call after Stage3 create packets with <see cref="Character"/> as activator.
    /// </summary>
    /// <returns>True if the trigger was findable and reactions were dispatched.</returns>
    public bool FireOnLoadPlayerMissions(ClonedObjectBase activator)
    {
        if (activator is null)
            return false;

        if (!TryGetPerPlayerLoadTrigger(out var trigger))
        {
            var coid = MapData.PerPlayerLoadTrigger;
            if (coid >= 0)
            {
                Logger.WriteLog(LogType.Debug,
                    "SectorMap {0}: PerPlayerLoad trigger {1} not findable — skip on-load grant",
                    ContinentId,
                    coid);
            }
            return false;
        }

        Logger.WriteLog(LogType.Debug,
            "SectorMap {0}: firing PlayerOnLoad trigger {1} ({2} reaction(s)) for activator {3}",
            ContinentId,
            trigger.ObjectId.Coid,
            trigger.Template.Reactions.Count,
            activator.ObjectId.Coid);

        TriggerReactions(activator, trigger.Template.Reactions.ToList());
        return true;
    }

    public void LeaveMap(ClonedObjectBase clonedObject)
    {
        if (clonedObject is Trigger trigger)
            Triggers.Remove(trigger.ObjectId);

        if (clonedObject is Reaction reaction)
            Reactions.Remove(reaction.ObjectId);

        if (clonedObject is Character character)
        {
            if (PlayerCount > 0)
                PlayerCount--;
            Players.Remove(character);
        }

        NpcAiEntities.Remove(clonedObject);

        Grid.Remove(clonedObject);

        if (!Objects.ContainsKey(clonedObject.ObjectId))
            throw new InvalidOperationException("This object is not on the map!");

        Objects.Remove(clonedObject.ObjectId);

        // Clear any trigger states for this object when it leaves the map
        TriggerManager.Instance.ClearTriggersFor(clonedObject.ObjectId.Coid);
    }

    // Reusable per-map scratch buffers for the scope query. The scope query runs per connection per
    // packet (>=100ms) and is single-threaded on the sector main loop, so sharing these avoids
    // per-call allocations. NOT thread-safe by design.
    private readonly List<ClonedObjectBase> _scopeNearby = new();
    private readonly List<ClonedObjectBase> _scopeMissionGivers = new();
    private readonly List<ClonedObjectBase> _scopeSelected = new();

    /// <summary>
    /// Distance/tier based interest management for one connection (replaces the old scope-everything
    /// <c>ObjectsInRange</c> full scan). Gathers players (Tier 1), nearby mission givers (Tier 2) and
    /// other nearby entities (Tier 3) from the spatial grid, runs <see cref="InterestSelector"/>, and
    /// puts the winners into scope. Hysteresis memory comes from TNL's own ghost bookkeeping via
    /// <see cref="GhostObject.IsGhostedTo"/>.
    /// </summary>
    public void PerformScopeQuery(GhostObject scopeObject, Character self, GhostConnection connection)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(connection);

        var center = self.CurrentVehicle?.Position ?? self.Position;

        // One grid pull covers both tiers; mission givers may sit out to the extended drop radius.
        var queryRadius = Math.Max(InterestSelector.BaseScopeDropRadius, InterestSelector.MissionGiverDropRadius);
        Grid.QueryRadius(center, queryRadius, _scopeNearby);

        _scopeMissionGivers.Clear();
        foreach (var entity in _scopeNearby)
        {
            if (entity is Creature { IsMissionGiver: true } and not Character)
                _scopeMissionGivers.Add(entity);
        }

        // List<Character> flows in as IReadOnlyList<ClonedObjectBase> via covariance — no copy.
        InterestSelector.Select(
            self,
            center,
            ContinentObject.IsTown,
            Players,
            _scopeMissionGivers,
            _scopeNearby,
            entity => entity.Ghost != null && entity.Ghost.IsGhostedTo(connection),
            _scopeSelected);

        foreach (var entity in _scopeSelected)
        {
            var ghost = entity.Ghost;
            if (ghost == null)
                continue;

            var foreignGlobalVehicle = entity is Vehicle vehicleEntity
                && entity.ObjectId.Global
                && !IsLocalPlayerVehicle(vehicleEntity, self);

            // The local vehicle has already been constructed by CreateVehicleExtended. Sending
            // GhostVehicle's initial update afterwards clears the client's wheelset reference
            // and crashes the renderer at FUN_004F5560.
            if (entity is Vehicle localVehicle && IsLocalPlayerVehicle(localVehicle, self))
                continue;

            if (foreignGlobalVehicle && !ScopeGlobalVehicles)
                continue;

            // Global game objects must exist in the client's object table before the TNL ghost
            // proxy attaches. Client FUN_008078b0: ghost object-create runs BEFORE game packets.
            // Ghost create uses a zeroed nest (wheel CBID 0). If we ObjectInScope while the client
            // no longer has the object (after ghost-kill), the client rebuilds from that blob and
            // owner-on arms Havok → AV 0x004F5566.
            //
            // Whenever the foreign vehicle is not currently ghosted, (re)send CreateVehicle and
            // hold ObjectInScope. FUN_00812630: missing TFID → full create with nest; existing TFID
            // + IsItemLink → re-apply nest; existing + !IsItemLink → no-op (safe if client kept it).
            TNL.TNLConnection gameConnection = connection as TNL.TNLConnection;
            var coid = entity.ObjectId.Coid;
            var notGhostedToConn = foreignGlobalVehicle && !ghost.IsGhostedTo(connection);
            if (foreignGlobalVehicle
                && ScopeGlobalVehicleCreate
                && gameConnection != null
                && notGhostedToConn
                && !gameConnection.HasActiveForeignCreateHold(coid))
            {
                // First sighting or re-scope after detach: ensure sector create before ghost.
                // Do NOT set IsItemLink here — client packet+0xA1 drives item-link UI (Red Brigade
                // tooltips on live). Nest recovery after a ghost race uses WheelSetMask delta instead.
                var createPacket = new CreateVehiclePacket();
                ((Vehicle)entity).WriteToPacket(createPacket);
                if (TNL.TNLConnection.ForceForeignCreateReapply)
                    createPacket.IsItemLink = true;
                gameConnection.SendGamePacket(createPacket);
                gameConnection.NoteForeignVehicleCreateSent(coid);
                if (((Vehicle)entity).WheelSet != null && ((Vehicle)entity).WheelSet.CBID > 0)
                    ghost.SetMaskBits(GhostVehicle.WheelSetMask);
                if (ScopeGlobalVehicleGhost)
                    continue;
            }

            if (foreignGlobalVehicle && !ScopeGlobalVehicleGhost)
                continue;

            if (foreignGlobalVehicle
                && gameConnection != null
                && !gameConnection.TryAllowForeignVehicleGhostScope(coid))
                continue;

            // First ObjectInScope after create hold: re-dirty wheel so the first ghost delta can
            // PackHardpoint/SetWheelset if CreateVehicle equip lost a race to the zero nest blob.
            var releasingCreateHold = foreignGlobalVehicle
                && gameConnection != null
                && gameConnection.HasActiveForeignCreateHold(coid);

            connection.ObjectInScope(ghost);
            if (foreignGlobalVehicle && gameConnection != null)
            {
                gameConnection.ClearForeignVehicleCreateHold(coid);
                if (releasingCreateHold
                    && entity is Vehicle scopedVeh
                    && scopedVeh.WheelSet != null
                    && scopedVeh.WheelSet.CBID > 0)
                {
                    ghost.SetMaskBits(GhostVehicle.WheelSetMask);
                }
            }
        }
    }

    /// <summary>True when <paramref name="vehicle"/> is the scoping character's current vehicle.</summary>
    internal static bool IsLocalPlayerVehicle(Vehicle vehicle, Character self)
    {
        if (vehicle == null || self == null)
            return false;
        return ReferenceEquals(vehicle, self.CurrentVehicle);
    }

    public void TriggerReactions(ClonedObjectBase activator, List<long> reactions)
    {
        TriggerReactionsInternal(activator, reactions, 0);
    }

    private void TriggerReactionsInternal(ClonedObjectBase activator, List<long> reactions, int depth)
    {
        const int MaxReactionDepth = 10;
        if (depth >= MaxReactionDepth)
        {
            Logger.WriteLog(LogType.Error, $"Reaction chain exceeded max depth of {MaxReactionDepth}, stopping to prevent infinite loop");
            return;
        }

        var clientPacket = new GroupReactionCallPacket();
        GroupReactionCallPacket broadcastPacket = null;
        GroupReactionCallPacket convoyPacket = null;
        var childReactionsToTrigger = new List<List<long>>();

        foreach (var reactionCoid in reactions)
        {
            var foundObj = Objects.FirstOrDefault(o => o.Key.Coid == reactionCoid && !o.Key.Global);
            if (foundObj.Value is not Reaction reaction)
            {
                Logger.WriteLog(LogType.Error, $"Map {MapData.ContinentObject.Id} tried to trigger reaction {reactionCoid}, but the Reaction object isn't found!");
                continue;
            }

            //Logger.WriteLog(LogType.Debug, $"Processing reaction {reactionCoid} ({reaction.Template.ReactionType}), depth={depth}");

            if (reaction.TriggerIfPossible(activator))
            {
                var packet = new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false);

                clientPacket.AddPacket(packet);

                if (reaction.Template.DoForAllPlayers)
                {
                    broadcastPacket ??= new GroupReactionCallPacket();
                    broadcastPacket.AddPacket(packet);
                }

                if (reaction.Template.DoForConvoy)
                {
                    convoyPacket ??= new GroupReactionCallPacket();
                    convoyPacket.AddPacket(packet);
                }

                // Queue child reactions for processing after this batch
                if (reaction.Template.Reactions.Count > 0)
                {
                    Logger.WriteLog(LogType.Debug, $"Reaction {reactionCoid} has {reaction.Template.Reactions.Count} child reactions to trigger");
                    childReactionsToTrigger.Add(reaction.Template.Reactions);
                }
            }
        }

        // Vehicle activators must resolve to owning character; empty batches skip send.
        // skipOpcode: true — client bitstream parser (0x6374F0) reads RPC payload from byte 0
        // as entry count; opcode is carried by TNL RPC type (same as MapInfoPacket).
        if (SendGroupReactionCall && clientPacket.Count > 0)
        {
            var character = ResolveCharacter(activator);
            character?.OwningConnection?.SendGamePacket(clientPacket, skipOpcode: true);
        }

        // Process child reactions after sending the parent reaction packets
        foreach (var childReactions in childReactionsToTrigger)
        {
            TriggerReactionsInternal(activator, childReactions, depth + 1);
        }

        if (SendGroupReactionCall && broadcastPacket is not null && broadcastPacket.Count > 0)
        {
            var exclude = ResolveCharacter(activator);
            foreach (var character in Objects.Values.OfType<Character>())
            {
                if (exclude is not null && ReferenceEquals(character, exclude))
                    continue;
                character.OwningConnection?.SendGamePacket(broadcastPacket, skipOpcode: true);
            }
        }

        if (convoyPacket is not null)
        {
            Logger.WriteLog(LogType.Debug, $"DoForConvoy GroupReactionCall skipped (no convoy system) on map {ContinentId}");
        }
    }

    /// <summary>Character that receives S2C reaction packets for this activator.</summary>
    internal static Character ResolveCharacter(ClonedObjectBase activator)
    {
        if (activator is null)
            return null;
        return activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
    }
}
