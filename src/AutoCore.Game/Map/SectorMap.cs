namespace AutoCore.Game.Map;

using System.Linq;
using global::TNL.Entities;
using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Mission.Requirements;
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

    /// <summary>
    /// Whether a map-load placement should call <see cref="SpawnPoint.Spawn"/> immediately.
    /// Inactive spawn points are still placed (so Create/Activate can find them) but do not
    /// materialize children until Create/Activate (client CVOGReaction_SpawnObject / SetObjectActiveState).
    /// Non-spawn templates always "spawn" in the sense of full placement.
    /// </summary>
    internal static bool ShouldSpawnChildrenAtMapLoad(ObjectTemplate template)
    {
        if (template is SpawnPointTemplate spawn)
            return spawn.OriginalIsActive;

        return true;
    }

    /// <summary>Unit-test seam for <see cref="InitializeLocalObjects"/> without AssetManager map load.</summary>
    internal void InitializeLocalObjectsForTests() => InitializeLocalObjects();

    private void InitializeLocalObjects()
    {
        // TODO: some objects are only needed on Sector? Global? Both?

        foreach (var template in MapData.Templates)
        {
            var obj = template.Value.Create();
            if (obj == null)
                continue;

            // CBID 0: map-logic objects (tests / incomplete templates) skip clonebase materialization.
            if (template.Value.CBID > 0)
                obj.LoadCloneBase(template.Value.CBID);

            obj.SetCoid(template.Value.COID, false);
            obj.Faction = template.Value.Faction;
            obj.Layer = template.Value.Layer;
            obj.SetMap(this);

            // Do NOT CreateGhost for all GraphicsObjects here — flooding the ghost table
            // with every map prop exhausts client ghost slots and NPCs stop appearing.
            // Combat props get ghosts lazily via MakeNotInvincible / first TakeDamage.

            // Always place the SpawnPoint entity; only materialize children when IsActive.
            if (obj is SpawnPoint sp && ShouldSpawnChildrenAtMapLoad(template.Value))
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
        // Idempotent entry: a re-enter/reconnect desync must not crash the tick. Check presence
        // first and no-op if already here, so PlayerCount/Players/Grid are not double-counted.
        if (Objects.ContainsKey(clonedObject.ObjectId))
        {
            Logger.WriteLog(LogType.Error,
                "SectorMap {0}: EnterMap ignoring object coid={1}; already on the map",
                ContinentId,
                clonedObject.ObjectId?.Coid ?? -1);
            return;
        }

        if (clonedObject is Trigger trigger)
            Triggers.Add(trigger.ObjectId, trigger);

        if (clonedObject is Reaction reaction)
            Reactions.Add(reaction.ObjectId, reaction);

        if (clonedObject is Character character)
        {
            PlayerCount++;
            Players.Add(character);
            character.MapPresence.EnsureContinent(ContinentId);

            // Solo enter: scrub leaked Final Exam combat vehicles and restore dialog NPCs.
            // Map-leave reset can miss if PlayerCount never hit 0 (stuck session / order bugs).
            if (PlayerCount == 1)
                ApplyAuthoredSpawnHygiene();

            // Mid-mission relog: after fam baseline, re-apply Creates for active deliver NPCs
            // and mission-conditioned setup. Vehicle may join the map a tick later — login path
            // also calls ApplyMissionPhaseWorldState after both character and vehicle SetMap.
            if (character.CurrentQuests.Count > 0)
                ApplyMissionPhaseWorldState(character.CurrentVehicle ?? (ClonedObjectBase)character);
        }

        if (HasNpcAi(clonedObject))
            NpcAiEntities.Add(clonedObject);

        Grid.Add(clonedObject);

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
        // Idempotent teardown: an object whose Map still points here but which is no longer in
        // Objects (e.g. dropped by a ResetLocalWorldToAuthored Objects.Clear under PlayerCount
        // drift) is already in the desired state. Never throw — a throw here propagates through
        // SetMap -> EndCharacterSession -> OnConnectionTerminated into the MainLoop tick and
        // aborts the rest of the disconnect teardown. Gate all mutation behind presence so we
        // do not spuriously decrement PlayerCount or remove a live sibling's bookkeeping.
        if (!Objects.ContainsKey(clonedObject.ObjectId))
        {
            Logger.WriteLog(LogType.Error,
                "SectorMap {0}: LeaveMap ignoring object coid={1}; not on the map (already removed)",
                ContinentId,
                clonedObject.ObjectId?.Coid ?? -1);
            return;
        }

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

        Objects.Remove(clonedObject.ObjectId);

        // Clear any trigger states for this object when it leaves the map
        TriggerManager.Instance.ClearTriggersFor(clonedObject.ObjectId.Coid);

        // Reaction Create/Delete mutate the live sector (delete standing Gunny, spawn combat
        // vehicle). MapData templates are process-global, so without a reset the next visitor
        // sees Final Exam mid-state while turning in earlier missions (Guns of the Expansion).
        if (clonedObject is Character && PlayerCount == 0 && !_resettingLocalWorld)
            ResetLocalWorldToAuthored();
    }

    bool _resettingLocalWorld;

    /// <summary>
    /// Solo-player hygiene: despawn children of fam-inactive spawns (combat Gunny pathing car)
    /// and re-materialize fam-active dialog spawns (standing Gunny) if missing.
    /// Does not re-run full map rebuild — safe while the entering character is already on the map.
    /// </summary>
    internal void ApplyAuthoredSpawnHygiene()
    {
        // 1) Despawn leaked reaction children from fam-inactive spawns (always on solo enter).
        foreach (var obj in Objects.Values.ToList())
        {
            if (obj is not SpawnPoint spawn)
                continue;

            if (spawn.Template.OriginalIsActive)
                continue;

            if (!spawn.HasLiveSpawn() && !spawn.MaterializedByReaction)
                continue;

            Logger.WriteLog(LogType.Debug,
                "SectorMap {0}: hygiene despawn children of inactive spawn coid={1} (reaction={2})",
                ContinentId,
                spawn.ObjectId.Coid,
                spawn.MaterializedByReaction ? 1 : 0);
            spawn.DespawnOwnedEntities();
            spawn.ClearReactionMaterializationState();
        }

        // 2) Restore fam-active dialog/mission spawns deleted by a prior initiate cascade.
        foreach (var kvp in MapData.Templates)
        {
            if (kvp.Value is not SpawnPointTemplate tpl || !tpl.OriginalIsActive)
                continue;

            var coid = kvp.Key;
            if (GetObjectByCoid(coid) is SpawnPoint existing)
            {
                if (!existing.HasLiveSpawn())
                {
                    Logger.WriteLog(LogType.Debug,
                        "SectorMap {0}: hygiene re-Spawn fam-active spawn coid={1}",
                        ContinentId,
                        coid);
                    existing.Spawn();
                }

                continue;
            }

            Logger.WriteLog(LogType.Debug,
                "SectorMap {0}: hygiene re-place fam-active spawn coid={1}",
                ContinentId,
                coid);

            ClonedObjectBase placed;
            try
            {
                placed = tpl.Create();
            }
            catch (Exception ex)
            {
                Logger.WriteLog(LogType.Error,
                    "SectorMap {0}: hygiene Create failed coid={1}: {2}",
                    ContinentId,
                    coid,
                    ex.Message);
                continue;
            }

            if (placed == null)
                continue;

            // Spawn markers often use a map-tool CBID; missing clonebase must not abort hygiene.
            if (tpl.CBID > 0)
            {
                try
                {
                    placed.LoadCloneBase(tpl.CBID);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Debug,
                        "SectorMap {0}: hygiene LoadCloneBase cbid={1} coid={2}: {3}",
                        ContinentId,
                        tpl.CBID,
                        coid,
                        ex.Message);
                }
            }

            placed.SetCoid(tpl.COID != 0 ? tpl.COID : coid, false);
            placed.Faction = tpl.Faction;
            placed.Layer = tpl.Layer;
            placed.SetMap(this);

            if (placed is SpawnPoint restored)
                restored.Spawn();
        }
    }

    /// <summary>
    /// After fam hygiene (or login), recreate map actors that retail left mid-mission via
    /// reaction Creates. Generic rules:
    /// <list type="number">
    /// <item>Mission-conditioned triggers (non-empty conditions that pass) listing Create
    /// reactions — fire those Creates so type 9/11/12 gates re-apply without movement.</item>
    /// <item>Active deliver objectives (<see cref="ObjectiveRequirementDeliver"/> with
    /// <c>NPCTargetCompletes</c>): if no live map entity has that CBID, fire Create reactions
    /// whose target SpawnPoint spawn-list type matches the CBID (pad turn-in NPCs created only
    /// via TE / DoOnActivate in continuous play).</item>
    /// </list>
    /// Does not create combat spawns unless their Create is condition-gated and currently true.
    /// </summary>
    /// <returns>Number of Create reactions dispatched.</returns>
    internal int ReplayMissionWorldSetup(ClonedObjectBase activator)
    {
        if (activator?.Map == null)
            return 0;

        var character = activator.GetAsCharacter() ?? activator.GetSuperCharacter(false);
        if (character == null || character.CurrentQuests.Count == 0)
            return 0;

        character.EnsureLogicVariables();

        var fired = 0;
        var firedCreateCoids = new HashSet<long>();

        // 1) Condition-gated Creates (mission vars). Require DoConditionals so pure Activate
        // remotes (e.g. l1_rem_gunnysioux_initiator with latch leftovers) are not re-fired.
        foreach (var trigger in Triggers.Values.ToList())
        {
            if (!trigger.Template.DoConditionals || trigger.Template.Conditions.Count == 0)
                continue;

            if (!trigger.ConditionsPass(activator))
                continue;

            var createCoids = CollectCreateReactionCoids(trigger.Template.Reactions);
            if (createCoids.Count == 0)
                continue;

            foreach (var coid in createCoids)
            {
                if (!firedCreateCoids.Add(coid))
                    continue;

                TriggerReactions(activator, new List<long> { coid });
                fired++;
            }
        }

        // 2) Deliver-CBID Creates (TE-only pad NPCs after restart hygiene).
        foreach (var deliverCbid in CollectActiveDeliverCbids(character))
        {
            if (MapHasLiveEntityWithCbid(deliverCbid))
                continue;

            foreach (var reaction in Reactions.Values.ToList())
            {
                if (reaction.Template.ReactionType != ReactionType.Create)
                    continue;

                if (!CreateTargetsSpawnType(reaction, deliverCbid))
                    continue;

                var coid = reaction.ObjectId.Coid;
                if (!firedCreateCoids.Add(coid))
                    continue;

                Logger.WriteLog(LogType.Debug,
                    "SectorMap {0}: replay Create {1} for missing deliver NPC cbid={2}",
                    ContinentId,
                    coid,
                    deliverCbid);
                TriggerReactions(activator, new List<long> { coid });
                fired++;
            }
        }

        if (fired > 0)
        {
            Logger.WriteLog(LogType.Debug,
                "SectorMap {0}: ReplayMissionWorldSetup fired {1} Create reaction(s) for coid={2}",
                ContinentId,
                fired,
                character.ObjectId.Coid);
        }

        return fired;
    }

    /// <summary>
    /// Mission re-eval + deliver/condition Create replay. Call after login create packets /
    /// PerPlayerLoad, and after solo hygiene when the character already holds quests.
    /// </summary>
    internal void ApplyMissionPhaseWorldState(ClonedObjectBase activator)
    {
        if (activator == null)
            return;

        TriggerManager.Instance.OnMissionStateChanged(activator);
        ReplayMissionWorldSetup(activator);
    }

    private List<long> CollectCreateReactionCoids(List<long> reactionCoids)
    {
        var result = new List<long>();
        foreach (var coid in reactionCoids)
        {
            if (GetObjectByCoid(coid) is Reaction { Template.ReactionType: ReactionType.Create })
                result.Add(coid);
        }

        return result;
    }

    private static IEnumerable<int> CollectActiveDeliverCbids(Character character)
    {
        var seen = new HashSet<int>();
        foreach (var quest in character.CurrentQuests)
        {
            var mission = AssetManager.Instance.GetMission(quest.MissionId);
            if (mission == null)
                continue;

            if (!mission.Objectives.TryGetValue(quest.ActiveObjectiveSequence, out var objective)
                || objective == null)
            {
                continue;
            }

            foreach (var req in objective.Requirements.OfType<ObjectiveRequirementDeliver>())
            {
                if (!req.NPCTargetCompletes || req.NPCTargetCBID <= 0)
                    continue;

                if (seen.Add(req.NPCTargetCBID))
                    yield return req.NPCTargetCBID;
            }
        }
    }

    private bool MapHasLiveEntityWithCbid(int cbid)
    {
        foreach (var obj in Objects.Values)
        {
            if (obj is Character)
                continue;

            if (obj.CBID == cbid)
                return true;
        }

        return false;
    }

    private bool CreateTargetsSpawnType(Reaction reaction, int spawnTypeOrCbid)
    {
        foreach (var objectCoid in reaction.Template.Objects)
        {
            SpawnPointTemplate tpl = null;
            if (GetObjectByCoid(objectCoid) is SpawnPoint liveSpawn)
                tpl = liveSpawn.Template;
            else if (MapData.Templates.TryGetValue(objectCoid, out var ot) && ot is SpawnPointTemplate mapTpl)
                tpl = mapTpl;

            if (tpl == null)
                continue;

            // Fam-active dialog spawns are restored by hygiene; only replay reaction pad spawns.
            if (tpl.OriginalIsActive)
                continue;

            foreach (var entry in tpl.Spawns)
            {
                if (entry.SpawnType == spawnTypeOrCbid && entry.SpawnType != -1)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuild map-local objects from fam-authored state. Call when the last player leaves so
    /// Create/Delete reaction side-effects do not persist for the next visit.
    /// </summary>
    internal void ResetLocalWorldToAuthored()
    {
        if (_resettingLocalWorld)
            return;

        // Root-cause guard: the wholesale Objects.Clear() below drops every entry, and the loop
        // deliberately skips SetMap(null) for Characters — so running while a Character is still
        // present would orphan it (Map set, but absent from Objects) and crash the next
        // SetMap(null). PlayerCount can drift out of sync with the real character set (see the
        // EnterMap PlayerCount==1 hygiene hook), so never trust PlayerCount==0 alone. Skipped
        // authored-state cleanup is recovered on the next solo enter via ApplyAuthoredSpawnHygiene.
        var stragglerCharacters = Objects.Values.Count(o => o is Character);
        if (stragglerCharacters > 0)
        {
            Logger.WriteLog(LogType.Error,
                "SectorMap {0}: skipping local-world reset; {1} character(s) still present (PlayerCount desync)",
                ContinentId,
                stragglerCharacters);
            return;
        }

        _resettingLocalWorld = true;
        try
        {
            // Snapshot: SetMap(null) mutates Objects via LeaveMap.
            var snapshot = Objects.Values.ToList();
            foreach (var obj in snapshot)
            {
                if (obj is Character)
                    continue;

                try
                {
                    obj.SetMap(null);
                }
                catch (Exception ex)
                {
                    Logger.WriteLog(LogType.Error,
                        "SectorMap {0}: reset remove coid={1} failed: {2}",
                        ContinentId,
                        obj.ObjectId?.Coid ?? -1,
                        ex.Message);
                }
            }

            Objects.Clear();
            Triggers.Clear();
            Reactions.Clear();
            NpcAiEntities.Clear();
            Players.Clear();
            PlayerCount = 0;
            Grid = new SpatialHashGrid();
            LocalCoidCounter = MapData.HighestCoid + 1;

            // Undo shared-template IsActive writes from older Create/Activate paths.
            foreach (var tpl in MapData.Templates.Values)
            {
                if (tpl is SpawnPointTemplate spawnTpl)
                    spawnTpl.IsActive = spawnTpl.OriginalIsActive;
            }

            InitializeLocalObjects();

            Logger.WriteLog(LogType.Debug,
                "SectorMap {0}: reset local world to fam-authored state (last player left)",
                ContinentId);
        }
        finally
        {
            _resettingLocalWorld = false;
        }
    }

    // Reusable per-map scratch buffers for the scope query. The scope query runs per connection per
    // packet (>=100ms) and is single-threaded on the sector main loop, so sharing these avoids
    // per-call allocations. NOT thread-safe by design.
    private readonly List<ClonedObjectBase> _scopeNearby = new();
    private readonly List<ClonedObjectBase> _scopeMissionGivers = new();
    private readonly List<ClonedObjectBase> _scopeSelected = new();
    // Not initialized inline: CreateForTests builds instances via GetUninitializedObject, which skips
    // field initializers (see the other _scope* buffers above, which tests re-initialize by reflection).
    // Self-initializing on first use avoids requiring every test call site to know about this field too.
    private HashSet<long> _scopePinnedSeenThisQuery;

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

        var gameConnection = connection as TNL.TNLConnection;
        _scopePinnedSeenThisQuery ??= new HashSet<long>();
        _scopePinnedSeenThisQuery.Clear();

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
                //
                // Chassis first, then CreateCreature with packet+0xF8 = vehicle COID.
                // CVOGCreature_PostCreateFromPacket only calls SetVehicle when the vehicle already
                // exists (FUN_004bafe0). Vehicle_applyCreatePacket owner attach is gated by map
                // +0xe4e8 and is not reliable for foreign NPCs. Bind-only ghost never materializes
                // nested 0x2013.
                var scopedForeign = (Vehicle)entity;
                var createPacket = new CreateVehiclePacket();
                scopedForeign.WriteToPacket(createPacket);
                if (TNL.TNLConnection.ForceForeignCreateReapply)
                    createPacket.IsItemLink = true;
                gameConnection.SendGamePacket(createPacket);
                ForeignNpcDriverWire.TrySendDriverCreate(gameConnection, scopedForeign);
                gameConnection.NoteForeignVehicleCreateSent(coid);
                if (scopedForeign.WheelSet != null && scopedForeign.WheelSet.CBID > 0)
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

            // P2: after first no-owner scope + delay, skip ObjectInScope once so TNL detaches
            // the ghost; next selection re-scopes with a full initial that may include owner.
            if (foreignGlobalVehicle
                && gameConnection != null
                && gameConnection.ShouldSkipForeignObjectInScopeForReghost(coid))
            {
                if (WireDiag.Enabled)
                {
                    Logger.WriteLog(LogType.Network,
                        "ForeignReghostDescope coid={0} {1}",
                        coid, gameConnection.FormatGhostingDiag());
                }

                continue;
            }

            // First ObjectInScope after create hold: re-dirty wheel so the first ghost delta can
            // PackHardpoint/SetWheelset if CreateVehicle equip lost a race to the zero nest blob.
            var releasingCreateHold = foreignGlobalVehicle
                && gameConnection != null
                && gameConnection.HasActiveForeignCreateHold(coid);

            var wasGhosted = foreignGlobalVehicle && ghost.IsGhostedTo(connection);
            // Pathing foreign vehicles: pin ScopeLocalAlways so PrepareWritePacket does not clear
            // InScope and Detach them between interest-query flaps (drops pose stream). Unpinned
            // via the sweep below once a coid stops qualifying, so TNL can eventually detach it.
            if (foreignGlobalVehicle
                && entity is Vehicle pathVehicle
                && pathVehicle.CoidCurrentPath > 0)
            {
                connection.ObjectLocalScopeAlways(ghost);
                gameConnection?.NotePathVehiclePinned(coid, ghost);
                _scopePinnedSeenThisQuery.Add(coid);
            }
            else
                connection.ObjectInScope(ghost);
            if (foreignGlobalVehicle && gameConnection != null)
            {
                var nowGhosted = ghost.IsGhostedTo(connection);
                // Diagnose CreateVehicle thrash with zero GhostPacks (owner-on investigation).
                if (WireDiag.Enabled && (releasingCreateHold || wasGhosted != nowGhosted))
                {
                    Logger.WriteLog(LogType.Network,
                        "ForeignGhostScope coid={0} wasGhosted={1} nowGhosted={2} releasingHold={3} {4}",
                        coid, wasGhosted ? 1 : 0, nowGhosted ? 1 : 0, releasingCreateHold ? 1 : 0,
                        gameConnection.FormatGhostingDiag());
                }

                if (nowGhosted)
                    gameConnection.NoteForeignVehicleGhostScoped(coid);

                gameConnection.ClearForeignVehicleCreateHold(coid);
                if (releasingCreateHold && entity is Vehicle scopedVeh)
                {
                    if (scopedVeh.WheelSet != null && scopedVeh.WheelSet.CBID > 0)
                        ghost.SetMaskBits(GhostVehicle.WheelSetMask);

                    // Client tick (FUN_008078b0): ghost object-create runs before game packets.
                    // Ghost nest is zero-filled; owner-on can ActivateEnterWorld before wheels stick
                    // (AV 0x004F5566). IsItemLink re-apply fixed that but caused Red Brigade tooltips.
                    // Instead: post-scope create without IsItemLink (full create if TFID missing;
                    // no-op if present) + WheelSetMask delta (SetWheelset path). No item-link UI.
                    var reapply = new CreateVehiclePacket();
                    scopedVeh.WriteToPacket(reapply);
                    reapply.IsItemLink = false;
                    gameConnection.SendGamePacket(reapply);
                    if (WireDiag.Enabled)
                    {
                        Logger.WriteLog(LogType.Network,
                            "ForeignGhostNestReapply coid={0} wheelCbid={1} isItemLink=0",
                            coid, scopedVeh.WheelSet?.CBID ?? -1);
                    }
                }

                // Target-frame Cur/Max (FUN_00838e20) needs vehicle+0xAC = driver via
                // CreateVehicle CoidCurrentOwner → SetVehicle. First create races the ghost
                // owner block that materializes the driver. IsItemLink re-apply re-runs nest
                // equip + tooltip UI (FUN_008024d0) but is the wrong path for owner attach
                // (live: tooltips with still-blank numbers). Schedule destroy+recreate instead.
                if (nowGhosted
                    && !wasGhosted
                    && entity is Vehicle ownerVeh
                    && TNL.TNLConnection.ShouldScheduleForeignOwnerAttachReapply(
                        gameConnection, ownerVeh.Owner?.GetAsCreature() != null))
                {
                    gameConnection.ScheduleForeignOwnerAttachReapply(coid);
                }
            }

            // Owner-attach recovery via ForeignNpcDriverWire (destroy veh+driver, recreate
            // vehicle→creature so PostCreate SetVehicle runs). See NPC.md §14.4.
            if (foreignGlobalVehicle
                && gameConnection != null
                && ghost.IsGhostedTo(connection)
                && gameConnection.TryConsumeForeignOwnerAttachReapply(coid)
                && entity is Vehicle attachVeh)
            {
                ForeignNpcDriverWire.TryExecuteOwnerAttachReapply(gameConnection, attachVeh, ghost);
            }
        }

        // Unpin path vehicles that no longer qualify for ScopeLocalAlways this pass (path ended,
        // vehicle left the grid, or despawned) so TNL's normal InScope-clearing/DetachObject flow
        // can reclaim them instead of holding them in scope for the life of the connection.
        if (gameConnection != null && gameConnection.PinnedPathVehicles.Count > 0)
        {
            List<long> stalePinned = null;
            foreach (var pinnedCoid in gameConnection.PinnedPathVehicles.Keys)
            {
                if (!_scopePinnedSeenThisQuery.Contains(pinnedCoid))
                    (stalePinned ??= new List<long>()).Add(pinnedCoid);
            }

            if (stalePinned != null)
            {
                foreach (var staleCoid in stalePinned)
                {
                    if (gameConnection.PinnedPathVehicles.TryGetValue(staleCoid, out var staleGhost))
                        connection.ObjectLocalClearAlways(staleGhost);

                    gameConnection.ClearPathVehiclePinned(staleCoid);
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
                // SingleClientOnly=true for activator-only presence mutations (Create/Delete/etc.).
                // DoForAllPlayers broadcast uses a separate packet; flag stays false there.
                var singleClientOnly = !reaction.Template.DoForAllPlayers;
                var packet = new LogicStateChangePacket(
                    reaction.ObjectId.Coid,
                    activator.ObjectId,
                    singleClientOnly);

                clientPacket.AddPacket(packet);

                if (reaction.Template.DoForAllPlayers)
                {
                    broadcastPacket ??= new GroupReactionCallPacket();
                    broadcastPacket.AddPacket(
                        new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false));
                }

                if (reaction.Template.DoForConvoy)
                {
                    convoyPacket ??= new GroupReactionCallPacket();
                    convoyPacket.AddPacket(
                        new LogicStateChangePacket(reaction.ObjectId.Coid, activator.ObjectId, false));
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
        // Soft-pedal: after dialog turn-in, hold 0x206C briefly so client interact FX / dialog
        // MSXML loads are not stacked with reaction UI (AV @ 0x007B6DB0). Server reactions already ran.
        if (SendGroupReactionCall && clientPacket.Count > 0)
        {
            var character = ResolveCharacter(activator);
            if (character != null
                && MissionClientSoftPedal.ShouldSuppressGroupReactionCall(character.ObjectId.Coid))
            {
                Logger.WriteLog(LogType.Debug,
                    "GroupReactionCall suppressed (mission UI soft-pedal) charCoid={0} entries={1}",
                    character.ObjectId.Coid,
                    clientPacket.Count);
            }
            else
            {
                character?.OwningConnection?.SendGamePacket(clientPacket, skipOpcode: true);
            }
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
                if (MissionClientSoftPedal.ShouldSuppressGroupReactionCall(character.ObjectId.Coid))
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
