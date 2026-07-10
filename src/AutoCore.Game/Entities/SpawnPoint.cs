namespace AutoCore.Game.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.EntityTemplates;
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

    public bool Spawn()
    {
        var spawnList = Template.GetSpawn();
        if (spawnList == null)
            return false;

        if (spawnList.IsTemplate)
        {
            var vehicleTemplate = AssetManager.Instance.GetVehicleTemplate(spawnList.SpawnType);
            if (vehicleTemplate == null)
                return false;

            return SpawnTemplateVehicle(vehicleTemplate, spawnList) != null;
        }

        var cloneBase = AssetManager.Instance.GetCloneBase(spawnList.SpawnType);
        if (cloneBase == null)
            return false;

        if (cloneBase.Type == CloneBaseObjectType.Creature)
        {
            if (SpawnCreature(cloneBase.CloneBaseSpecific.CloneBaseId, spawnList) == null)
                return false;
        }
        else if (cloneBase.Type == CloneBaseObjectType.Vehicle)
        {
            if (SpawnVehicle(cloneBase.CloneBaseSpecific.CloneBaseId, spawnList) == null)
                return false;
        }
        else
            Logger.WriteLog(LogType.Error, $"SpawnPoint {Template.COID} wants to spawn object with type {cloneBase.Type}!");

        return true;
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
    /// Combat (<c>IsNPC == 0</c>) is left at the map spawn Y; the client AI re-snaps them.
    /// See <c>Documentation/NPC_SPAWN_HEIGHT.md</c>.
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
        // TODO: faction of the creature should be the faction of the spawnpoint?

        var creature = new Creature();
        // Global=true + high COID range: see MapNpcIdentity (client crash 0x005D262A).
        var counter = Map.LocalCoidCounter;
        AssignMapNpcIdentity(creature, ref counter);
        Map.LocalCoidCounter = counter;
        creature.LoadCloneBase(cbid);
        creature.SetupCBFields();

        // Flag mission-relevant NPCs once at spawn so interest management can grant them the
        // extended scope radius (data-driven from the mission set; no hardcoded ids).
        creature.IsMissionGiver = NpcInteractHandler.IsMissionGiverCbid(cbid);

        var cloneBaseCreature = creature.CloneBaseObject as CloneBaseCreature;
        if (cloneBaseCreature != null)
        {
            var baseLevel = cloneBaseCreature.CreatureSpecific.BaseLevel;
            creature.Level = CalculateSpawnLevel(baseLevel, spawnList.LevelOffset);
            creature.ScaleHealthForLevel((byte)baseLevel);
            creature.Position = ApplyStaticNpcSpawnHeight(
                Position,
                cloneBaseCreature.CreatureSpecific,
                cloneBaseCreature.SimpleObjectSpecific.PhysicsName);
        }
        else
        {
            creature.Level = 1;
            creature.Position = Position;
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
        vehicle.Layer = Layer;
        vehicle.Position = Position;
        vehicle.Rotation = Rotation;

        // Raw-CBID spawn lists have no VehicleTemplate row, so the driver can only come from
        // the vehicle clonebase's own VehicleSpecific.DefaultDriver.
        var cloneBaseVehicle = vehicle.CloneBaseObject as CloneBaseVehicle;
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
        vehicle.TemplateId = template.Id;
        vehicle.SpawnOwnerCoid = ObjectId.Coid;
        vehicle.Layer = Layer;
        vehicle.Position = Position;
        vehicle.Rotation = Rotation;
        vehicle.ApplyTemplateBaseHp(template.BaseHp);

        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponFront, template.WeaponFrontCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponTurret, template.WeaponTurretCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.WeaponMelee, template.WeaponMeleeCbid);
        EquipTemplateItem(vehicle, VehicleEquipmentSlot.Armor, template.ArmorCbid);

        var cloneBaseVehicle = vehicle.CloneBaseObject as CloneBaseVehicle;
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

        var baseLevel = (driver.CloneBaseObject as CloneBaseCreature)?.CreatureSpecific.BaseLevel ?? 1;
        driver.Level = CalculateSpawnLevel(baseLevel, levelOffset);
        driver.Position = Position;
        driver.Rotation = Rotation;
        driver.Layer = Layer;

        return driver;
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

        SimpleObject item = slot == VehicleEquipmentSlot.Armor ? new Armor() : new Weapon();
        item.SetCoid(Map.LocalCoidCounter++, false);
        item.LoadCloneBase(cbid);
        item.SetupCBFields();

        vehicle.TryEquipItem(slot, item, out _);
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
