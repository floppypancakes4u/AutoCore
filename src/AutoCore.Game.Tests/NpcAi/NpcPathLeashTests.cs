using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.NpcAi;

using System.Runtime.CompilerServices;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Npc;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Path-aware leash/return anchor (client parity <c>CVOGHBAIDriver::ReturnToNormalLocation</c>
/// @ 005d6e80): an NPC with an assigned map path returns to the nearest point on its path, not to
/// its spawn (the waypoint <c>+0x52</c> path-vs-spawn branch). A pathless NPC still leashes to spawn.
/// </summary>
[TestClass]
public class NpcPathLeashTests
{
    private const int ContId = 861;
    private const int DriverCbid = 86_100;
    private const int WeaponCbid = 86_200;
    private const long PathCoid = 86_010;

    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = (_, _) => { };
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        Vehicle.ClearCombatThrottleForTests();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void PathNpc_OnPathFarFromSpawn_DoesNotLeash()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f));

        // NPC sits on its path waypoint (500u from spawn) — far past the 80u leash radius from spawn.
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 0f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(505f, 0f, 0f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.EngageStartedMs = 100_000;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreSame(player, npc.Target, "a path NPC on its path must NOT leash off the attacker");
        Assert.AreEqual(HBAICombatState.Engage, npc.NpcAi.CombatState, "stays engaged, no return-to-spawn");
        Assert.IsFalse(npc.NpcAi.ReturningHome, "path NPC near its path does not flag ReturningHome");
    }

    [TestMethod]
    public void PathNpc_InCombat_RidesPathWhileFiring()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f), new Vector3(500f, 0f, 40f));

        // NPC on waypoint 0 with a target in weapon range: it should fire AND advance along its path.
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 0f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        npc.NpcAi.PathIndex = 0;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(500f, 0f, 20f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual((byte)1, npc.Firing, "fires at the in-range target during combat");
        Assert.AreSame(player, npc.Target, "keeps its target");
        Assert.IsFalse(npc.NpcAi.PursuingThisTick, "an in-range target is not chased off the path");
        Assert.AreEqual(1, npc.NpcAi.PathIndex, "keeps riding its path (waypoint cursor advanced)");
    }

    [TestMethod]
    public void PathNpc_TargetOffPathButNear_LungesTowardTarget()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f));

        // NPC near its path (10u), target out of weapon range (30) but within vision (60): lunge.
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 10f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 30f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(555f, 0f, 10f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.IsTrue(npc.NpcAi.PursuingThisTick, "a reachable off-range target near the path is pursued");
        Assert.IsTrue(npc.Position.X > 500f, "lunges toward the target at X=555");
        Assert.AreSame(player, npc.Target, "keeps its target while lunging");
    }

    [TestMethod]
    public void PathNpc_StrayedFromPath_ReturnsToPath_KeepsTarget_NotSpawn()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f), new Vector3(500f, 0f, 40f));

        // Strayed 260u from the nearest path point but the target is still within vision (40 <= 60):
        // it must ride back toward the PATH (not spawn) without dropping the target or leashing.
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 300f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 30f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        npc.NpcAi.PathIndex = -1;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(500f, 0f, 340f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.1f);

        Assert.AreSame(player, npc.Target, "a strayed path NPC keeps its target (no distance leash)");
        Assert.AreEqual(HBAICombatState.Combat, npc.NpcAi.CombatState, "stays engaged");
        Assert.IsFalse(npc.NpcAi.PursuingThisTick, "does not chase further while strayed off its path");
        Assert.AreEqual(500f, npc.Position.X, 0.001f, "returns along the path (X=500), not toward spawn X=0");
        Assert.IsTrue(npc.Position.Z < 300f, "moves back toward the path point at Z=40");
    }

    [TestMethod]
    public void PathNpc_TargetBeyondVision_DisengagesAndReturnsToPath()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f));

        // Target 190u away, past the NPC's 60u vision: it drops the target and heads back to its path.
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 10f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 30f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(500f, 0f, 200f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.IsNull(npc.Target, "target beyond perception range is dropped");
        Assert.AreEqual(HBAICombatState.IdlePatrol, npc.NpcAi.CombatState, "returns to patrol");
        Assert.IsTrue(npc.NpcAi.ReturningHome, "heads back to its path");
    }

    [TestMethod]
    public void PathlessNpc_KitedBeyondLeash_ReturnsToSpawn_Unchanged()
    {
        var map = CreateFieldMap();

        // No CoidCurrentPath assigned → anchor is the spawn, behavior unchanged.
        var npc = PlaceNpcVehicle(map, new Vector3(200f, 0f, 0f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        var (player, _) = PlacePlayerVehicle(map, new Vector3(205f, 0f, 0f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Combat;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);
        Assert.IsNull(npc.Target, "pathless NPC still leashes when dragged past 80u from spawn");
        Assert.IsTrue(npc.NpcAi.ReturningHome);

        NpcCombatAi.Tick(map, npc, nowMs: 100_100, dt: 0.1f);
        Assert.IsTrue(npc.Position.X < 200f, "pathless NPC steers back toward spawn at the origin");
    }

    [TestMethod]
    public void PathNpc_Fleeing_RunsToNearestPathPoint_NotSpawn()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f), new Vector3(500f, 0f, 40f));

        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 300f), driverFaction: 3, speed: 10f);
        EquipFrontWeapon(npc, rangeMax: 50f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        var (player, _) = PlacePlayerVehicle(map, new Vector3(500f, 0f, 305f), faction: 0);

        npc.SetTargetObject(player);
        npc.NpcAi.CombatState = HBAICombatState.Engage;
        npc.NpcAi.FleeUntilMs = 200_000; // fleeing at nowMs 100_000
        npc.Firing = 1;

        NpcCombatAi.Tick(map, npc, nowMs: 100_000, dt: 0.1f);

        Assert.AreEqual((byte)0, npc.Firing, "a fleeing NPC holds fire");
        Assert.AreEqual(500f, npc.Position.X, 0.001f, "flees along the path (X=500), not toward spawn X=0");
        Assert.IsTrue(npc.Position.Z < 300f, "flees toward the nearest path point at Z=40");
    }

    [TestMethod]
    public void PathNpc_AfterReturn_ResumesPatrol()
    {
        var map = CreateFieldMap();
        SeedPath(map, PathCoid, new Vector3(500f, 0f, 0f), new Vector3(500f, 0f, 40f));

        // NPC has walked back to within ResumePathRadius (5u) of path point P1 (500,0,40).
        var npc = PlaceNpcVehicle(map, new Vector3(500f, 0f, 42f), driverFaction: 3, speed: 10f);
        npc.NpcAi.HomePosition = new Vector3(0f, 0f, 0f);
        npc.CoidCurrentPath = PathCoid;
        npc.NpcAi.CombatState = HBAICombatState.IdlePatrol;
        npc.NpcAi.ReturningHome = true;
        npc.NpcAi.PathIndex = -1;

        NpcTicker.Tick(map, nowMs: 100_000, dt: 0.1f);

        Assert.IsFalse(npc.NpcAi.ReturningHome, "arrived at the path anchor, done returning");
        Assert.IsTrue(npc.NpcAi.PathIndex >= 0, "patrol re-latched to a path node and resumed");
    }

    // ----- helpers -------------------------------------------------------------------------

    private static SectorMap CreateFieldMap()
    {
        var continent = new AutoCore.Database.World.Models.ContinentObject
        {
            Id = ContId,
            MapFileName = $"tm_npc_path_leash_{ContId}",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }

    private static MapPathTemplate SeedPath(SectorMap map, long coid, params Vector3[] points)
    {
        var path = new MapPathTemplate { COID = (int)coid, ReverseDirection = false };
        foreach (var p in points)
            path.Points.Add(new MapPathTemplate.MapPathPoint { Position = p, AcceptDistance = 1f });
        map.MapData.Templates[coid] = path;
        return path;
    }

    private Vehicle PlaceNpcVehicle(SectorMap map, Vector3 position, int driverFaction, float speed)
    {
        var driverCbid = DriverCbid + (int)map.LocalCoidCounter;
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid);
        var driverSpec = AssetManager.Instance.GetCloneBase<CloneBaseCreature>(driverCbid).CreatureSpecific;
        driverSpec.VisionRange = 60f;
        driverSpec.Speed = speed;

        var driver = new Creature();
        driver.LoadCloneBase(driverCbid);
        driver.SetCoid(map.LocalCoidCounter++, false);
        driver.Faction = driverFaction;

        var profile = new CreatureAiProfile { AiId = 1 };
        profile.Vals[0] = 8000f; // engage timer — stay in Engage this tick

        var vehicle = new Vehicle();
        vehicle.SetCoid(map.LocalCoidCounter++, false);
        vehicle.Position = position;
        vehicle.SetOwner(driver);
        vehicle.NpcAi = new NpcAiState { Profile = profile, HomePosition = position };
        vehicle.SetMap(map);
        return vehicle;
    }

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
        character.SetCurrentVehicleForTests(vehicle);
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
        weapon.SetCoid(9_998_001, false);
        typeof(ClonedObjectBase).GetProperty(nameof(ClonedObjectBase.CloneBaseObject))!
            .SetValue(weapon, cloneBase);

        vehicle.TryEquipItem(VehicleEquipmentSlot.WeaponFront, weapon, out _);
    }
}
