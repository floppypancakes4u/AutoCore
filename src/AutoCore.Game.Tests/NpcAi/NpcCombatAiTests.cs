using Microsoft.VisualStudio.TestTools.UnitTesting;
using TNL.Entities;

namespace AutoCore.Game.Tests.NpcAi;

using System.Linq;
using System.Runtime.CompilerServices;
using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;

/// <summary>
/// Stage 10: <see cref="NpcCombatAi"/> mirrors the client HBAIDriver DoLogic loop — acquire a
/// hostile target from the spatial grid (IdlePatrol), close the distance (Engage), fire the
/// player combat pipeline (Combat), and leash home when dragged too far from the anchor.
/// </summary>
[TestClass]
public class NpcCombatAiTests
{
    private const int ContId = 850;
    private const int DriverCbid = 85_100;
    private const int WeaponCbid = 85_200;

    private readonly List<(TNLConnection Conn, BasePacket Packet)> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (c, p) => _sent.Add((c, p));
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        _sent.Clear();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void IdlePatrol_HostilePlayerInVisionRange_AcquiresTargetAndEngages()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (player, _) = PlacePlayerVehicle(map, new Vector3(30f, 0f, 0f), faction: 0);

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreSame(player, npc.Target, "the hostile player vehicle in vision range must be acquired");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState, "acquiring a target enters Engage");
        Assert.AreEqual(100_000L, npc.NpcAi.EngageStartedMs, "engage start timestamp must be stamped on acquire");
        Assert.IsFalse(npc.NpcAi.HelpCalled, "HelpCalled resets when a fresh engagement begins");
    }

    [TestMethod]
    public void IdlePatrol_FriendlyOrNeutral_NoAggro()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);

        // A same-faction ally (faction 3) and a neutral (-100) both sit well within vision range.
        PlaceNpcVehicle(map, new Vector3(10f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var neutral = PlaceNpcVehicle(map, new Vector3(12f, 0f, 0f), driverFaction: -100, visionRange: 60f);

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.IsNull(npc.Target, "friendly (same faction) and neutral neighbours must not be aggroed");
        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState, "friendly/neutral neighbours must not trigger aggro");
        Assert.IsNull(neutral.Target, "neutral NPC must not aggro the patrolling NPC either");
    }

    /// <summary>
    /// Ambient (21) wildlife (e.g. Ark Bay Osterake) proactively scans and engages players —
    /// not "neutral until attacked" (NPC.md §15.4).
    /// </summary>
    [TestMethod]
    public void IdlePatrol_AmbientDriver_AggroesPlayerInVision()
    {
        var map = CreateFieldMap();
        var ambient = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 21, visionRange: 60f);
        var (player, _) = PlacePlayerVehicle(map, new Vector3(30f, 0f, 0f), faction: 0);

        NpcCombatAi.Tick(map, ambient, nowMs: 100_000, dt: 0.1f);

        Assert.AreSame(player, ambient.Target, "Ambient NPC must acquire the player in vision");
        Assert.AreEqual(HBAICombatState.Engage, ambient.NpcAi.CombatState);
    }

    [TestMethod]
    public void IdlePatrol_NeutralDriver_DoesNotAggroPlayerInVision()
    {
        var map = CreateFieldMap();
        var neutral = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: -100, visionRange: 60f);
        PlacePlayerVehicle(map, new Vector3(10f, 0f, 0f), faction: 0);

        NpcCombatAi.Tick(map, neutral, nowMs: 100_000, dt: 0.1f);

        Assert.IsNull(neutral.Target, "Neutral (−100) must never proactive-aggro");
        Assert.AreEqual(HBAICombatState.IdlePatrol, neutral.NpcAi.CombatState);
    }

    [TestMethod]
    public void Engage_TimerElapsed_TransitionsToCombat()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f, engageTimerMs: 2000f);
        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.EngageStartedMs = 100_000L - 3000L; // 3s elapsed > 2s timer

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual(HBAICombatState.Combat, npc.NpcAi.CombatState, "engage timer elapsed → Combat");
        Assert.AreSame(player, npc.Target, "target is retained through the Engage→Combat transition");
    }

    [TestMethod]
    public void Combat_OutOfRange_PursuesTowardTarget_AtDriverSpeed()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 10f);
        var (player, _) = PlacePlayerVehicle(map, new Vector3(100f, 0f, 0f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.5f);

        // speed 10 * dt 0.5 = 5 units toward (100,0,0).
        Assert.AreEqual(5f, npc.Position.X, 0.01f, "out-of-range NPC must pursue toward the target at driver speed");
        Assert.AreEqual(0f, npc.Position.Z, 0.01f, "pursuit stays on the straight line to the target");
        Assert.AreEqual((byte)0, npc.Firing, "no weapon fires while out of range");
    }

    [TestMethod]
    public void Combat_InRange_SetsFiringBitForEquippedWeapon_AndInvokesCombat()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.CreateGhost(); // ProcessCombatIfFiring requires a ghost
        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);
        player.ApplyTemplateBaseHp(100_000); // survive a single hit so OnDeath doesn't fire

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual((byte)1, npc.Firing, "in-range NPC must raise the front-weapon firing bit (1)");
        Assert.AreSame(player, npc.Target, "the fired-upon target must be set for the combat pipeline");
    }

    [TestMethod]
    public void Combat_BeyondPatrolDistance_LeashesHome_ClearsTargetAndFiring()
    {
        var map = CreateFieldMap();
        var npc = PlaceNpcVehicle(map, new Vector3(200f, 0f, 0f), driverFaction: 3, visionRange: 60f, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f); // 200u from home > 80u leash radius
        var (player, _) = PlacePlayerVehicle(map, new Vector3(205f, 0f, 0f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;
        npc.Firing = 1;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.IsNull(npc.Target, "leashing must drop the combat target");
        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState, "leashing returns to IdlePatrol");
        Assert.IsTrue(npc.NpcAi.ReturningHome, "leashing flags the NPC to walk back home");
        Assert.AreEqual((byte)0, npc.Firing, "leashing ceases fire");
    }

    [TestMethod]
    public void StateChange_SetsStateAndTargetGhostMasks()
    {
        var map = CreateFieldMap();
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var driver = (Creature)npc.Owner;
        npc.CreateGhost();
        connection.ActivateGhosting();
        connection.ObjectLocalScopeAlways(npc.Ghost);
        var ghostInfo = npc.Ghost.GetFirstObjectRef();
        Assert.IsNotNull(ghostInfo, "expected the NPC vehicle ghost to be scoped");

        // State change dirties the vehicle StateMask and mirrors onto the driver's wire state byte.
        ghostInfo.UpdateMask = 0;
        NpcCombatAi.SetCombatState(npc, HBAICombatState.Combat);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(GhostVehicle.StateMask, ghostInfo.UpdateMask & GhostVehicle.StateMask,
            "combat-state change must dirty the vehicle StateMask");
        Assert.AreEqual((byte)HBAICombatState.Combat, driver.AiCombatState,
            "the driver creature carries the wire AI-state byte for the vehicle");

        // Target change dirties the TargetMask (via ClonedObjectBase.SetTargetObject).
        var (player, _) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);
        ghostInfo.UpdateMask = 0;
        npc.SetTargetObject(player);
        NetObject.CollapseDirtyList();

        Assert.AreEqual(GhostObject.TargetMask, ghostInfo.UpdateMask & GhostObject.TargetMask,
            "acquiring a target must dirty the TargetMask");
    }

    [TestMethod]
    public void TrySendDamagePacket_NpcAttacker_DeliversToVictimConnection()
    {
        var map = CreateFieldMap();
        // NPC attacker vehicle: no owning character, so the pre-refactor code delivered nothing.
        var npc = PlaceNpcVehicle(map, new Vector3(0f, 0f, 0f), driverFaction: 3, visionRange: 60f);
        var (victim, connection) = PlacePlayerVehicle(map, new Vector3(5f, 0f, 0f), faction: 0);

        Vehicle.TrySendDamagePacket(attacker: null, victim: victim, source: npc.ObjectId, actualDamage: 42);

        var delivered = _sent
            .Where(e => ReferenceEquals(e.Conn, connection) && e.Packet is DamagePacket)
            .Select(e => (DamagePacket)e.Packet)
            .ToList();
        Assert.AreEqual(1, delivered.Count, "victim's connection must receive exactly one NPC damage packet");
        Assert.AreEqual(npc.ObjectId.Coid, delivered[0].Source.Coid, "damage packet source is the attacker vehicle");
        Assert.AreEqual(victim.ObjectId.Coid, delivered[0].Entries[0].Target.Coid, "damage packet targets the victim");
    }

    // ----- helpers -------------------------------------------------------------------------

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_combat_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    /// <summary>NPC vehicle owned by a ghostless driver creature that carries faction/vision/speed.</summary>
    private Vehicle PlaceNpcVehicle(
        SectorMap map,
        Vector3 position,
        int driverFaction,
        float visionRange,
        float hearingRange = 0f,
        float speed = 5f,
        float engageTimerMs = 8000f)
    {
        var driverCbid = DriverCbid + (int)map.LocalCoidCounter; // unique clonebase per driver
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid);
        var driverSpec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(driverCbid).CreatureSpecific;
        driverSpec.VisionRange = visionRange;
        driverSpec.HearingRange = hearingRange;
        driverSpec.Speed = speed;

        var driver = new Creature();
        driver.LoadCloneBase(driverCbid);
        driver.SetCoid(map.LocalCoidCounter++, false);
        driver.Faction = driverFaction;

        var profile = new CreatureAiProfile { AiId = 1 };
        profile.Vals[0] = engageTimerMs;

        var vehicle = new Vehicle();
        vehicle.SetCoid(map.LocalCoidCounter++, false);
        vehicle.Position = position;
        vehicle.SetOwner(driver);
        vehicle.NpcAi = new NpcAiState { Profile = profile, HomePosition = position };
        vehicle.SetMap(map);
        return vehicle;
    }

    /// <summary>Player vehicle owned by a connected character carrying a player-race faction.</summary>
    private (Vehicle Vehicle, TNLConnection Connection) PlacePlayerVehicle(SectorMap map, Vector3 position, int faction)
    {
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(map.LocalCoidCounter++, true);
        character.Faction = faction;
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(map.LocalCoidCounter++, true);
        vehicle.Position = position;
        character.SetCurrentVehicleForTests(vehicle); // sets vehicle.Owner = character
        vehicle.SetMap(map);
        return (vehicle, connection);
    }

    private static void EquipFrontWeapon(Vehicle vehicle, float rangeMax)
    {
        var spec = new WeaponSpecific
        {
            RangeMin = 0f,
            RangeMax = rangeMax,
            RechargeTime = 1,
            DamageScalar = 1f,
            DmgMinMin = 1,
            DmgMaxMax = 2,
            MinMin = DamageSpecific.CreateEmpty(),
            MaxMax = DamageSpecific.CreateEmpty(),
        };
        var cloneBase = (CloneBaseWeapon)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseWeapon));
        cloneBase.WeaponSpecific = spec;
        cloneBase.SimpleObjectSpecific = new SimpleObjectSpecific();
        cloneBase.CloneBaseSpecific = new CloneBaseSpecific { CloneBaseId = WeaponCbid, Type = (int)CloneBaseObjectType.Weapon };

        var weapon = new Weapon();
        weapon.SetCoid(9_999_001, false);
        typeof(ClonedObjectBase).GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(weapon, cloneBase);

        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _);
    }
}
