namespace AutoCore.Game.Entities;

using AutoCore.Game.Constants;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
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

        // TODO: load templates from wad.xml and use them?
        if (spawnList.IsTemplate)
            return false;

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
            if (SpawnVehicle(cloneBase.CloneBaseSpecific.CloneBaseId) == null)
                return false;

            // TODO: spawn driver also (driverid in templates)
        }
        else
            Logger.WriteLog(LogType.Error, $"SpawnPoint {Template.COID} wants to spawn object with type {cloneBase.Type}!");

        return true;
    }

    /// <summary>
    /// Applies map-NPC COID policy (global + high range) used by <see cref="SpawnCreature"/>.
    /// Exposed for regression tests of the 0x005D262A crash fix.
    /// </summary>
    internal static void AssignMapNpcIdentity(Creature creature, ref long localCoidCounter)
    {
        ArgumentNullException.ThrowIfNull(creature);
        var objectId = MapNpcIdentity.AllocateCoid(ref localCoidCounter);
        creature.SetCoid(objectId.Coid, objectId.Global);
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
        creature.SetMap(Map);
        creature.CreateGhost();

        return creature;
    }

    private Vehicle SpawnVehicle(int cbid)
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(Map.LocalCoidCounter++, false);
        vehicle.LoadCloneBase(cbid);
        vehicle.SetupCBFields();
        vehicle.Layer = Layer;
        vehicle.Position = Position;
        vehicle.Rotation = Rotation;
        vehicle.SetMap(Map);
        vehicle.CreateGhost();

        return vehicle;
    }
}
