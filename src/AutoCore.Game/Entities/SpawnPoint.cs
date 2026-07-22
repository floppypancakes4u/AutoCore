namespace AutoCore.Game.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Inventory;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Utils;

public class SpawnPoint : ClonedObjectBase
{
    // NOTE: Let's not duplicate this data, if we don't need to create new spawnpoints manually!
    public SpawnPointTemplate Template { get; }

    /// <summary>
    /// COID of the most recent creature/vehicle produced by <see cref="Spawn"/> (0 if none).
    /// Used by Create/Delete lifecycle to know whether a live child still exists.
    /// </summary>
    public long LastSpawnedCoid { get; private set; }

    /// <summary>
    /// True when the last <see cref="Spawn"/> call requested TriggerEvents fire
    /// (Create/Activate only; map load leaves this false).
    /// </summary>
    internal bool LastSpawnRequestedFireTriggerEvents { get; private set; }

    /// <summary>
    /// True when children were materialized by a Create/Activate reaction this map session
    /// (not fam map-load). Used to detect leaked combat spawns after a previous visit.
    /// </summary>
    internal bool MaterializedByReaction { get; private set; }

    /// <summary>
    /// Distance (world units) at which deferred TriggerEvents may flush. Create reactions that
    /// place objects farther than this from the activator wait until a player approaches —
    /// so drop-in / air-drop Create animations play near the target, not off-screen at combat
    /// start (e.g. combat spawn TE → pad Create while the player is still ~90u away).
    /// </summary>
    internal const float TriggerEventProximity = 45f;

    /// <summary>True after TriggerEvents have been dispatched (or determined empty).</summary>
    internal bool AuthoredTriggerEventsConsumed { get; private set; }

    /// <summary>True while Create targets are out of range and TE is waiting for approach.</summary>
    internal bool HasDeferredAuthoredTriggerEvents { get; private set; }

    long _deferredTeActivatorCoid;

    /// <summary>
    /// Foot-to-origin height for the shared humanoid body (<c>creature.cache</c> /
    /// <c>humanoid.cache</c> in physics.glm). Half-extent ≈ 1.1803. See
    /// <c>Documentation/NPC_SPAWN_HEIGHT.md</c> and client
    /// <c>CVOGCreature_FindTerrainHeight</c> / <c>CVOGSpawnPoint_CreateCreature</c>.
    /// </summary>
    internal const float CreaturePhysicsFootOffset = 1.1803f;

    public override int GetBareTeamFaction() => Faction;
    public override int GetCurrentHP() => CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;
    public override int GetMaximumHP() => CloneBaseObject.SimpleObjectSpecific.MaxHitPoint;

    public SpawnPoint(SpawnPointTemplate template)
    {
        Template = template;
    }

    public override void CreateGhost()
    {
        // NO GHOST!
    }

    /// <summary>True when <see cref="LastSpawnedCoid"/> still resolves to an object on the current map.</summary>
    public bool HasLiveSpawn()
    {
        if (LastSpawnedCoid == 0 || Map == null)
            return false;

        return Map.GetObjectByCoid(LastSpawnedCoid) != null;
    }

    /// <summary>Unit-test helper when spawn runs without clonebase-backed <see cref="Spawn"/>.</summary>
    internal void SetLastSpawnedCoidForTests(long coid) => LastSpawnedCoid = coid;

    /// <summary>
    /// Remove creatures/vehicles that list this spawn COID as their spawn owner.
    /// Called when a Delete reaction removes the SpawnPoint (client RemoveObject path).
    /// Includes dialog IsNPC / mission givers — Final Exam deletes standing Gunny with the
    /// spawn marker (l1_del_gunnysioux1). Preserving them left the human prop after accept.
    /// </summary>
    public void DespawnOwnedEntities()
    {
        if (Map == null)
            return;

        var spawnCoid = ObjectId.Coid;
        // Snapshot: SetMap(null) mutates Map.Objects while we iterate.
        var owned = new List<ClonedObjectBase>();
        foreach (var kvp in Map.Objects)
        {
            var obj = kvp.Value;
            if (obj is Creature creature && creature.SpawnOwner == spawnCoid)
                owned.Add(creature);
            else if (obj is Vehicle vehicle && vehicle.SpawnOwnerCoid == spawnCoid)
                owned.Add(vehicle);
        }

        foreach (var obj in owned)
        {
            Logger.WriteLog(LogType.Debug,
                "SpawnPoint {0}: despawning owned {1} coid={2}",
                spawnCoid,
                obj.GetType().Name,
                obj.ObjectId.Coid);
            obj.SetMap(null);
        }

        LastSpawnedCoid = 0;
    }

    /// <summary>
    /// Materialize spawn children. Map load uses defaults (no TriggerEvents).
    /// Create/Activate reactions pass <paramref name="fireTriggerEvents"/> true so authored
    /// events run only for reaction-driven spawns (e.g. 14138 → 15818 Create pad Gunny).
    /// Firing TriggerEvents on every map-load Spawn can cascade Delete of active dialog NPCs
    /// (standing Gunny 14090) before the player accepts the mission.
    /// </summary>
    /// <param name="fireTriggerEvents">When true, fire template TriggerEvents after success.</param>
    /// <param name="triggerActivator">Reaction activator (player/vehicle) for condition context.</param>
    public bool Spawn(bool fireTriggerEvents = false, ClonedObjectBase triggerActivator = null)
    {
        LastSpawnRequestedFireTriggerEvents = fireTriggerEvents;

        var spawnList = Template.GetSpawn();
        if (spawnList == null)
            return false;

        if (spawnList.IsTemplate)
        {
            var vehicleTemplate = AssetManager.Instance.GetVehicleTemplate(spawnList.SpawnType);
            if (vehicleTemplate == null)
                return false;

            var templateVehicle = SpawnTemplateVehicle(vehicleTemplate, spawnList);
            if (templateVehicle == null)
                return false;

            LastSpawnedCoid = templateVehicle.ObjectId.Coid;
            if (fireTriggerEvents)
            {
                MaterializedByReaction = true;
                FireAuthoredTriggerEvents(triggerActivator ?? templateVehicle);
            }

            return true;
        }

        var cloneBase = AssetManager.Instance.GetCloneBase(spawnList.SpawnType);
        if (cloneBase == null)
            return false;

        if (cloneBase.Type == CloneBaseObjectType.Creature)
        {
            var creature = SpawnCreature(cloneBase.CloneBaseSpecific.CloneBaseId, spawnList);
            if (creature == null)
                return false;

            LastSpawnedCoid = creature.ObjectId.Coid;
            if (fireTriggerEvents)
            {
                MaterializedByReaction = true;
                FireAuthoredTriggerEvents(triggerActivator ?? creature);
            }

            return true;
        }

        if (cloneBase.Type == CloneBaseObjectType.Vehicle)
        {
            var vehicle = SpawnVehicle(cloneBase.CloneBaseSpecific.CloneBaseId, spawnList);
            if (vehicle == null)
                return false;

            LastSpawnedCoid = vehicle.ObjectId.Coid;
            if (fireTriggerEvents)
            {
                MaterializedByReaction = true;
                FireAuthoredTriggerEvents(triggerActivator ?? vehicle);
            }

            return true;
        }

        Logger.WriteLog(LogType.Error, $"SpawnPoint {Template.COID} wants to spawn object with type {cloneBase.Type}!");
        return false;
    }

    /// <summary>Clear reaction-materialization bookkeeping after a hygiene despawn.</summary>
    internal void ClearReactionMaterializationState()
    {
        MaterializedByReaction = false;
        AuthoredTriggerEventsConsumed = false;
        HasDeferredAuthoredTriggerEvents = false;
        _deferredTeActivatorCoid = 0;
        LastSpawnedCoid = 0;
    }

    /// <summary>
    /// Fire map-authored <see cref="ObjectTemplate.TriggerEvents"/> (reaction-driven spawn only).
    /// Each positive COID is resolved as a map <see cref="Trigger"/> and dispatched via
    /// <see cref="TriggerManager.FireTriggerReactions"/> (e.g. Final Exam combat spawn 14138 →
    /// trigger 15818 Create pad Gunny 15820). Negative / zero slots are ignored.
    /// <para>
    /// When any Create target lies farther than <see cref="TriggerEventProximity"/> from the
    /// activator, the fire is deferred until <see cref="TryFlushDeferredAuthoredTriggerEvents"/>
    /// (player movement) so client Create animations (air-drop, etc.) play on approach.
    /// </para>
    /// </summary>
    /// <param name="activator">Prefer the reaction activator (player); falls back to this spawn marker.</param>
    /// <param name="forceImmediate">Skip proximity deferral (used after approach flush).</param>
    public void FireAuthoredTriggerEvents(ClonedObjectBase activator = null, bool forceImmediate = false)
    {
        if (AuthoredTriggerEventsConsumed)
            return;

        var events = Template?.TriggerEvents;
        if (events == null || events.Length == 0 || Map == null)
        {
            AuthoredTriggerEventsConsumed = true;
            HasDeferredAuthoredTriggerEvents = false;
            return;
        }

        var act = activator ?? this;
        if (act.Map == null)
            act = this;

        if (!forceImmediate && ShouldDeferAuthoredTriggerEvents(act, events))
        {
            HasDeferredAuthoredTriggerEvents = true;
            _deferredTeActivatorCoid = act.ObjectId?.Coid ?? 0;
            Logger.WriteLog(LogType.Debug,
                "SpawnPoint {0}: deferring TriggerEvents until activator within {1:0} of Create targets (activatorCoid={2})",
                ObjectId?.Coid ?? Template.COID,
                TriggerEventProximity,
                _deferredTeActivatorCoid);
            return;
        }

        AuthoredTriggerEventsConsumed = true;
        HasDeferredAuthoredTriggerEvents = false;
        _deferredTeActivatorCoid = 0;

        for (var i = 0; i < events.Length; i++)
        {
            var triggerCoid = events[i];
            if (triggerCoid <= 0)
                continue;

            var trigger = Map.GetObjectByCoid(triggerCoid) as Trigger
                ?? (Map.Triggers.TryGetValue(new TFID(triggerCoid, false), out var t) ? t : null);
            if (trigger == null)
            {
                Logger.WriteLog(LogType.Debug,
                    "SpawnPoint {0}: TriggerEvent[{1}] coid={2} not on map — skip",
                    ObjectId?.Coid ?? Template.COID,
                    i,
                    triggerCoid);
                continue;
            }

            if (trigger.Template?.Reactions == null || trigger.Template.Reactions.Count == 0)
                continue;

            Logger.WriteLog(LogType.Debug,
                "SpawnPoint {0}: firing TriggerEvent[{1}] trigger={2} reactions=[{3}]",
                ObjectId?.Coid ?? Template.COID,
                i,
                triggerCoid,
                string.Join(',', trigger.Template.Reactions));
            TriggerManager.Instance.FireTriggerReactions(act, trigger);
        }
    }

    /// <summary>
    /// If TriggerEvents were deferred (Create targets out of range), fire them when
    /// <paramref name="activator"/> is now close enough.
    /// </summary>
    public void TryFlushDeferredAuthoredTriggerEvents(ClonedObjectBase activator)
    {
        if (AuthoredTriggerEventsConsumed || !HasDeferredAuthoredTriggerEvents || activator == null)
            return;

        var events = Template?.TriggerEvents;
        if (events == null || events.Length == 0)
        {
            AuthoredTriggerEventsConsumed = true;
            HasDeferredAuthoredTriggerEvents = false;
            return;
        }

        if (ShouldDeferAuthoredTriggerEvents(activator, events))
            return;

        Logger.WriteLog(LogType.Debug,
            "SpawnPoint {0}: flushing deferred TriggerEvents (activatorCoid={1})",
            ObjectId?.Coid ?? Template.COID,
            activator.ObjectId?.Coid ?? -1);
        FireAuthoredTriggerEvents(activator, forceImmediate: true);
    }

    /// <summary>
    /// Spawn-child death path: request authored TE if not yet consumed (deferred or immediate).
    /// Generic — any spawn with TriggerEvents can stage follow-up Creates after the child dies.
    /// </summary>
    public void NotifySpawnedChildDied(ClonedObjectBase dyingChild, ClonedObjectBase killerOrActivator)
    {
        if (AuthoredTriggerEventsConsumed)
            return;

        var act = killerOrActivator ?? dyingChild ?? this;
        FireAuthoredTriggerEvents(act);
    }

    bool ShouldDeferAuthoredTriggerEvents(ClonedObjectBase activator, long[] events)
    {
        if (activator?.Map == null || Map == null || events == null)
            return false;

        var nearestCreateSq = float.MaxValue;
        var foundCreateTarget = false;

        for (var i = 0; i < events.Length; i++)
        {
            var triggerCoid = events[i];
            if (triggerCoid <= 0)
                continue;

            var trigger = Map.GetObjectByCoid(triggerCoid) as Trigger
                ?? (Map.Triggers.TryGetValue(new TFID(triggerCoid, false), out var t) ? t : null);
            if (trigger?.Template?.Reactions == null)
                continue;

            foreach (var reactionCoid in trigger.Template.Reactions)
            {
                if (Map.GetObjectByCoid(reactionCoid) is not Reaction reaction)
                    continue;
                if (reaction.Template?.ReactionType != ReactionType.Create)
                    continue;

                foreach (var objectCoid in reaction.Template.Objects)
                {
                    if (!TryGetMapTemplatePosition(Map, objectCoid, out var pos))
                        continue;

                    foundCreateTarget = true;
                    var dSq = activator.Position.DistSq(pos);
                    if (dSq < nearestCreateSq)
                        nearestCreateSq = dSq;
                }
            }
        }

        if (!foundCreateTarget)
            return false;

        var limit = TriggerEventProximity;
        return nearestCreateSq > limit * limit;
    }

    static bool TryGetMapTemplatePosition(SectorMap map, long coid, out Vector3 position)
    {
        var live = map.GetObjectByCoid(coid);
        if (live != null)
        {
            position = live.Position;
            return true;
        }

        if (map.MapData.Templates.TryGetValue(coid, out var template)
            && template is GraphicsObjectTemplate graphics)
        {
            position = graphics.Location.ToVector3();
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// Applies the map-NPC COID policy (global + high range) to spawned creatures and vehicles.
    /// Exposed for regression tests of client-local map-object identity collisions.
    /// </summary>
    internal static void AssignMapNpcIdentity(ClonedObjectBase npc, ref long localCoidCounter)
    {
        ArgumentNullException.ThrowIfNull(npc);
        var objectId = MapNpcIdentity.AllocateCoid(ref localCoidCounter);
        npc.SetCoid(objectId.Coid, objectId.Global);
    }

    /// <summary>
    /// Clamps spawn level into the valid byte range (1..255).
    /// </summary>
    internal static byte CalculateSpawnLevel(int baseLevel, int levelOffset)
    {
        var calculatedLevel = baseLevel + levelOffset;
        return (byte)Math.Max(1, Math.Min(255, calculatedLevel));
    }

    /// <summary>
    /// Resolves the driver CBID for a spawned vehicle: prefer the vehicle-template's explicit
    /// driver, else fall back to the vehicle clonebase's <c>VehicleSpecific.DefaultDriver</c>.
    /// </summary>
    internal static int ResolveDriverCbid(int templateDriverCbid, int defaultDriverCbid)
    {
        return templateDriverCbid > 0 ? templateDriverCbid : defaultDriverCbid;
    }

    /// <summary>
    /// Applies MapPathCoid/InitialPatrolDistance/ReverseDirection from the spawn-point template
    /// (and resolved <see cref="MapPathTemplate"/>) onto a spawned combat creature.
    /// MapPathCoid &lt;= 0 leaves the creature's path fields at their defaults.
    /// </summary>
    internal static void ApplySpawnPath(Creature creature, SpawnPointTemplate template, MapPathTemplate path)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ResolveSpawnPath(template, path, out var coid, out var patrol, out var reversing);
        creature.CoidCurrentPath = coid;
        creature.PatrolDistance = patrol;
        creature.PathReversing = reversing;
    }

    /// <summary>Vehicle counterpart of <see cref="ApplySpawnPath(Creature, SpawnPointTemplate, MapPathTemplate)"/>.</summary>
    internal static void ApplySpawnPath(Vehicle vehicle, SpawnPointTemplate template, MapPathTemplate path)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ResolveSpawnPath(template, path, out var coid, out var patrol, out var reversing);
        vehicle.CoidCurrentPath = coid;
        vehicle.PatrolDistance = patrol;
        vehicle.PathReversing = reversing;
    }

    private static void ResolveSpawnPath(
        SpawnPointTemplate template,
        MapPathTemplate path,
        out long coid,
        out float patrol,
        out bool reversing)
    {
        if (template == null || template.MapPathCoid <= 0)
        {
            coid = -1;
            patrol = 0f;
            reversing = false;
            return;
        }

        coid = template.MapPathCoid;
        patrol = template.InitialPatrolDistance;
        reversing = path?.ReverseDirection ?? false;
    }

    /// <summary>
    /// Elevates static interactive NPCs so the body origin sits above terrain.
    /// Combat (<c>IsNPC == 0</c>) is left at the map spawn Y (caller may pure-snap via
    /// <see cref="NpcTicker.SnapToTerrain"/>). See <c>Documentation/NPC_SPAWN_HEIGHT.md</c>.
    /// </summary>
    internal static Vector3 ApplyStaticNpcSpawnHeight(
        Vector3 spawnPosition,
        CreatureSpecific creatureSpecific,
        string physicsName)
    {
        if (creatureSpecific == null || creatureSpecific.IsNPC == 0)
            return spawnPosition;

        var scale = creatureSpecific.PhysicsScale;
        if (scale <= 0f || !float.IsFinite(scale))
            scale = 1f;

        var y = spawnPosition.Y
            + creatureSpecific.FlyingHeight
            + ResolvePhysicsFootOffset(physicsName) * scale;

        return new Vector3(spawnPosition.X, y, spawnPosition.Z);
    }

    /// <summary>
    /// Foot offset for known physics bodies. Interactive NPCs almost always use
    /// <c>creature</c> (or empty PhysicsName, treated the same).
    /// </summary>
    internal static float ResolvePhysicsFootOffset(string physicsName)
    {
        if (string.IsNullOrWhiteSpace(physicsName))
            return CreaturePhysicsFootOffset;

        if (physicsName.Equals("creature", StringComparison.OrdinalIgnoreCase) ||
            physicsName.Equals("humanoid", StringComparison.OrdinalIgnoreCase))
            return CreaturePhysicsFootOffset;

        return 0f;
    }

    private Creature SpawnCreature(int cbid, SpawnPointTemplate.SpawnList spawnList)
    {
        var creature = new Creature();
        // Global=true + high COID range: see MapNpcIdentity (client crash 0x005D262A).
        var counter = Map.LocalCoidCounter;
        AssignMapNpcIdentity(creature, ref counter);
        Map.LocalCoidCounter = counter;
        creature.LoadCloneBase(cbid);
        creature.SetupCBFields();
        ApplySpawnFactionOverride(creature);

        // Flag mission-relevant NPCs once at spawn so interest management can grant them the
        // extended scope radius (data-driven from the mission set; no hardcoded ids).
        creature.IsMissionGiver = NpcInteractHandler.IsMissionGiverCbid(cbid);

        var cloneBaseCreature = creature.CloneBaseObject as CloneBaseCreature;
        if (cloneBaseCreature != null)
        {
            var baseLevel = cloneBaseCreature.CreatureSpecific.BaseLevel;
            creature.Level = CalculateSpawnLevel(baseLevel, spawnList.LevelOffset);
            creature.ScaleHealthForLevel((byte)baseLevel);
            // IsNPC: map spawn Y + foot (see ApplyStaticNpcSpawnHeight). Combat: pure heightfield
            // when present — no foot (server ghosts use XYZ as-is; +foot floats them).
            creature.Position = ApplyStaticNpcSpawnHeight(
                Position,
                cloneBaseCreature.CreatureSpecific,
                cloneBaseCreature.SimpleObjectSpecific.PhysicsName);
            if (cloneBaseCreature.CreatureSpecific.IsNPC == 0)
                creature.Position = NpcTicker.SnapToTerrain(Map, creature.Position);
        }
        else
        {
            creature.Level = 1;
            creature.Position = NpcTicker.SnapToTerrain(Map, Position);
            Logger.WriteLog(LogType.Error,
                $"SpawnPoint.SpawnCreature: Creature CBID={cbid} is not a CloneBaseCreature, defaulting to level 1");
        }

        creature.Layer = Layer;
        creature.Rotation = Rotation;
        creature.SpawnOwner = ObjectId.Coid;

        // Static/interactive NPCs (IsNPC != 0) don't patrol or run combat AI; combat creatures do.
        if (cloneBaseCreature != null && cloneBaseCreature.CreatureSpecific.IsNPC == 0)
        {
            ApplySpawnPath(creature, Template, ResolveTemplatePath());
            creature.NpcAi = BuildNpcAi(cloneBaseCreature.CreatureSpecific.AIBehavior, creature.Position);
        }

        creature.SetMap(Map);
        creature.CreateGhost();

        return creature;
    }

    private Vehicle SpawnVehicle(int cbid, SpawnPointTemplate.SpawnList spawnList)
    {
        var vehicle = new Vehicle();
        var counter = Map.LocalCoidCounter;
        AssignMapNpcIdentity(vehicle, ref counter);
        Map.LocalCoidCounter = counter;
        vehicle.LoadCloneBase(cbid);
        vehicle.SetupCBFields();
        ApplySpawnFactionOverride(vehicle);
        vehicle.Layer = Layer;
        // Pure terrain when heightfield present (CreateTemplateVehicle cast).
        vehicle.Position = NpcTicker.SnapToTerrain(Map, Position);
        vehicle.Rotation = Rotation;
        // Same as template path: chassis clonebase invincible bit must not block combat.
        vehicle.SetInvincible(false);

        // Raw-CBID spawn lists have no VehicleTemplate row, so the driver can only come from
        // the vehicle clonebase's own VehicleSpecific.DefaultDriver.
        var cloneBaseVehicle = vehicle.CloneBaseObject as CloneBaseVehicle;
        EquipDefaultWheelSet(vehicle, cloneBaseVehicle);
        var defaultDriverCbid = cloneBaseVehicle?.VehicleSpecific.DefaultDriver ?? 0;
        var driver = BuildDriver(0, defaultDriverCbid, spawnList.LevelOffset);
        if (driver != null)
        {
            vehicle.SetOwner(driver);
            ApplyDriverAi(vehicle, driver);
        }

        ApplySpawnPath(vehicle, Template, ResolveTemplatePath());

        // vehicle.NpcAi (set above via ApplyDriverAi, if any) must be assigned before SetMap so
        // EnterMap's HasNpcAi check sees it and registers the vehicle in Map.NpcAiEntities.
        vehicle.SetMap(Map);
        vehicle.CreateGhost();

        return vehicle;
    }

    /// <summary>
    /// Spawns a vehicle from a wad.xml <c>tVehicleTemplate</c> row (spawn-list SpawnType is the
    /// template id, not a vehicle CBID; see <see cref="SpawnPointTemplate.SpawnList.IsTemplate"/>).
    /// </summary>
    private Vehicle SpawnTemplateVehicle(VehicleTemplate template, SpawnPointTemplate.SpawnList spawnList)
    {
        var vehicle = new Vehicle();
        var counter = Map.LocalCoidCounter;
        AssignMapNpcIdentity(vehicle, ref counter);
        Map.LocalCoidCounter = counter;
        vehicle.LoadCloneBase(template.VehicleCbid);
        vehicle.SetupCBFields();
        ApplySpawnFactionOverride(vehicle);
        vehicle.TemplateId = template.Id;
        vehicle.SpawnOwnerCoid = ObjectId.Coid;
        vehicle.Layer = Layer;
        vehicle.Position = NpcTicker.SnapToTerrain(Map, Position);
        vehicle.Rotation = Rotation;
        vehicle.ApplyTemplateBaseHp(template.BaseHp);
        // Clonebase may flag chassis as invincible; template NPC vehicles are combat targets
        // (e.g. Final Exam Gunny). Client MakeNotInvincible is not always authored — clear here.
        vehicle.SetInvincible(false);

        var cloneBaseVehicle = vehicle.CloneBaseObject as CloneBaseVehicle;
        EquipDefaultWheelSet(vehicle, cloneBaseVehicle);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponFront, template.WeaponFrontCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponTurret, template.WeaponTurretCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponMelee, template.WeaponMeleeCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.Armor, template.ArmorCbid);

        var defaultDriverCbid = cloneBaseVehicle?.VehicleSpecific.DefaultDriver ?? 0;
        var driver = BuildDriver(template.DriverCbid, defaultDriverCbid, spawnList.LevelOffset);
        if (driver != null)
        {
            vehicle.SetOwner(driver);
            ApplyDriverAi(vehicle, driver);
        }

        ApplySpawnPath(vehicle, Template, ResolveTemplatePath());

        // vehicle.NpcAi (set above via ApplyDriverAi, if any) must be assigned before SetMap so
        // EnterMap's HasNpcAi check sees it and registers the vehicle in Map.NpcAiEntities.
        vehicle.SetMap(Map);
        vehicle.CreateGhost();

        return vehicle;
    }

    /// <summary>
    /// Builds the (unmapped, ghostless) driver creature that supplies a vehicle's combat level
    /// and faction chain (<see cref="ClonedObjectBase.GetIDFaction"/>) — client builds the
    /// HBAIDriver contract from the vehicle owner's CBID + level (NPC.md §8.5).
    /// Returns null when no driver CBID is available (template nor clonebase default).
    /// </summary>
    private Creature BuildDriver(int templateDriverCbid, int defaultDriverCbid, int levelOffset)
    {
        var driverCbid = ResolveDriverCbid(templateDriverCbid, defaultDriverCbid);
        if (driverCbid <= 0)
            return null;

        var driver = new Creature();
        var counter = Map.LocalCoidCounter;
        AssignMapNpcIdentity(driver, ref counter);
        Map.LocalCoidCounter = counter;
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();
        ApplySpawnFactionOverride(driver);

        var baseLevel = (driver.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific.BaseLevel ?? 1;
        driver.Level = CalculateSpawnLevel(baseLevel, levelOffset);
        driver.Position = Position;
        driver.Rotation = Rotation;
        driver.Layer = Layer;

        return driver;
    }

    /// <summary>
    /// When the map spawn has <see cref="SpawnPointTemplate.FactionDirty"/> set, copy the
    /// authored spawn faction onto the child (client <c>FUN_00512460</c> after
    /// <c>Object_GetRootRaceId(spawnpoint)</c> — NPC.md §15.3).
    /// Prefers live spawnpoint / template <see cref="ClonedObjectBase.Faction"/>, then fam
    /// <see cref="SpawnPointTemplate.OriginalFaction"/> when Faction was left at default 0/-1
    /// (Human / unset) — otherwise mission combat NPCs share Human faction and weapons skip them.
    /// No-op when dirty is false; clonebase faction from <see cref="SimpleObject.SetupCBFields"/> remains.
    /// </summary>
    private void ApplySpawnFactionOverride(ClonedObjectBase entity)
    {
        if (entity == null || Template == null || !Template.FactionDirty)
            return;

        entity.Faction = ResolveFactionDirtyOverride();
    }

    /// <summary>
    /// Resolves FactionDirty override: live spawnpoint Faction, else template Faction,
    /// else fam <see cref="SpawnPointTemplate.OriginalFaction"/>.
    /// </summary>
    internal int ResolveFactionDirtyOverride()
    {
        if (Template == null)
            return Faction;

        // Neutral (-100) and positive mission factions are valid; -1 is ClonedObjectBase unset.
        if (Faction != -1 && (Faction != 0 || Template.OriginalFaction == 0))
            return Faction;

        if (Template.Faction != -1 && (Template.Faction != 0 || Template.OriginalFaction == 0))
            return Template.Faction;

        return Template.OriginalFaction;
    }

    /// <summary>Copies the driver's wad.xml AI behavior onto the vehicle it owns, if any.</summary>
    private static void ApplyDriverAi(Vehicle vehicle, Creature driver)
    {
        var aiId = (driver?.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific.AIBehavior ?? 0;
        vehicle.NpcAi = BuildNpcAi(aiId, vehicle.Position);
    }

    /// <summary>Resolves a wad.xml tCreatureAI profile (AIBehavior/AIID) into runtime NPC AI state.</summary>
    private static NpcAiState BuildNpcAi(int aiBehaviorId, Vector3 homePosition)
    {
        if (aiBehaviorId <= 0)
            return null;

        var profile = AssetManager.Instance.GetCreatureAiProfile(aiBehaviorId);
        if (profile == null)
            return null;

        return new NpcAiState { Profile = profile, HomePosition = homePosition };
    }

    /// <summary>Equips a template weapon/armor CBID into a vehicle slot, if the CBID is set (&gt; 0).</summary>
    private void EquipTemplateItem(Vehicle vehicle, VehicleEquipmentSlot slot, int cbid)
    {
        if (cbid <= 0)
            return;

        SimpleObject item = slot switch
        {
            VehicleEquipmentSlot.Armor => new Armor(),
            VehicleEquipmentSlot.WheelSet => new WheelSet(),
            _ => new Weapon(),
        };
        // Nested CreateVehicle equipment TFIDs are resolved on the client before GiveItemByCbid.
        // Low Global=false COIDs collide with client-local map objects and skip materialization,
        // leaving vehicle+0x258 null → Havok AV 0x004F5566 when owner/ghost activates.
        var counter = Map.LocalCoidCounter;
        var objectId = MapNpcIdentity.AllocateCoid(ref counter);
        Map.LocalCoidCounter = counter;
        item.SetCoid(objectId.Coid, objectId.Global);
        item.LoadCloneBase(cbid);
        item.SetupCBFields();

        // Some retail rows list a front-only CBID under CBIDWeaponTurret (e.g. Template 196 /
        // CBID 13952 Flags=0x03). Hardpoint Flags are authoritative: equip the legal mount, not the
        // wrong column. Never force a front-only weapon onto the turret hardpoint.
        if (!TryApplyTemplateEquip(vehicle, slot, item, out var equipSlot, out var skippedOccupied))
        {
            if (!skippedOccupied)
            {
                Logger.WriteLog(LogType.Error,
                    $"SpawnPoint {Template.COID}: template vehicle (TemplateId={vehicle.TemplateId}) failed to equip slot={equipSlot} (templateCol={slot}) itemCbid={cbid}");
            }
        }
    }

    /// <summary>
    /// Maps a template equipment column to a hardpoint the item may legally occupy.
    /// Weapons use <see cref="VehicleEquipmentSlotResolver"/> when the column mismatches Flags.
    /// </summary>
    internal static VehicleEquipmentSlot ResolveTemplateEquipSlot(VehicleEquipmentSlot templateColumn, SimpleObject item)
    {
        if (item is not Weapon weapon || weapon.CloneBaseWeapon == null)
            return templateColumn;

        if (Vehicle.IsCompatibleWithEquipmentSlot(templateColumn, item, out _))
            return templateColumn;

        // dropPositionX=0 is only a fallback when Flags are empty; prefer Flags bits first.
        if (VehicleEquipmentSlotResolver.TryResolveWeaponSlot(weapon.CloneBaseWeapon, dropPositionX: 0, out var resolved))
            return resolved;

        return templateColumn;
    }

    /// <summary>
    /// Applies template-column equip rules: remap by hardpoint Flags, skip overwrite of occupied
    /// remapped slots. Returns false when equip failed or was skipped because remapped target full.
    /// </summary>
    internal static bool TryApplyTemplateEquip(
        Vehicle vehicle,
        VehicleEquipmentSlot templateColumn,
        SimpleObject item,
        out VehicleEquipmentSlot equipSlot,
        out bool skippedOccupied)
    {
        skippedOccupied = false;
        equipSlot = ResolveTemplateEquipSlot(templateColumn, item);
        if (equipSlot != templateColumn && vehicle.GetEquippedItem(equipSlot) != null)
        {
            skippedOccupied = true;
            return false;
        }

        return vehicle.TryEquipItem(equipSlot, item, out _);
    }

    /// <summary>
    /// NPC vehicles do not have character-inventory rows to supply equipment. Their clonebase
    /// default wheelset is therefore required in the nested CreateVehicle payload before the
    /// client can safely construct or render the vehicle.
    /// </summary>
    private void EquipDefaultWheelSet(Vehicle vehicle, CloneBaseVehicle cloneBaseVehicle)
    {
        var wheelsetCbid = cloneBaseVehicle?.VehicleSpecific.DefaultWheelset ?? 0;
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WheelSet, wheelsetCbid);
    }

    /// <summary>Resolves this spawn point's <see cref="SpawnPointTemplate.MapPathCoid"/> on the current map.</summary>
    private MapPathTemplate ResolveTemplatePath()
    {
        if (Template.MapPathCoid <= 0)
            return null;

        Map.TryGetMapPath(Template.MapPathCoid, out var path);
        return path;
    }
}
