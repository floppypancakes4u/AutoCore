using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.EntityTemplates;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using AutoCore.Game.TNL.Ghost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

/// <summary>
/// Pass 17 — destination spawn children (capacity, pose, ghosts, scope, respawn).
/// Retail <c>FUN_00566490</c> refills every filled slot independently to Lower..Upper
/// (capped at 10) and heartbeats on RespawnTime milliseconds.
/// </summary>
[TestClass]
public class DestinationSpawnTests
{
    private const int CreatureCbid = 770_001;
    private const int OtherCreatureCbid = 770_002;
    private const int VehicleCbid = 770_010;
    private const int DriverCbid = 770_011;
    private const int WheelsetCbid = 770_012;
    private const int TemplateId = 770_020;

    [TestInitialize]
    public void TestInitialize()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.TestPacketSink = null;
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
    }

    [TestCleanup]
    public void TestCleanup()
    {
        AssetManager.Instance.ClearTestNpcData();
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.TestPacketSink = null;
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    [TestMethod]
    public void FamSpawnPoint_InitiallyActive_SpawnsChild()
    {
        var map = CreateTestMap(9701);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_101, CreatureCbid, lower: 1, upper: 1);
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();

        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsNotNull(spawn);
        Assert.IsTrue(spawn!.HasLiveSpawn());
        Assert.AreEqual(1, CountMappedCreatures(map));
    }

    [TestMethod]
    public void FamSpawnPoint_InitiallyInactive_DoesNotSpawn()
    {
        var map = CreateTestMap(9702);
        RegisterWalkingCreature(CreatureCbid);
        var template = new SpawnPointTemplate
        {
            COID = 20_102,
            IsActive = false,
            OriginalIsActive = false,
        };
        template.Spawns.Add(FilledSlot(CreatureCbid, 2, 2));
        map.MapData.Templates[template.COID] = template;

        map.InitializeLocalObjectsForTests();

        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsNotNull(spawn, "inactive marker must still be placed");
        Assert.IsFalse(spawn!.HasLiveSpawn());
        Assert.AreEqual(0, CountMappedCreatures(map));
    }

    [TestMethod]
    public void SpawnPoint_ChildUsesRetailCbid()
    {
        var map = CreateTestMap(9703);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_103, CreatureCbid, 1, 1));

        Assert.IsTrue(spawn.Spawn());
        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreEqual(CreatureCbid, child.CBID);
    }

    [TestMethod]
    public void SpawnPoint_ChildPositionMatchesRetailPose()
    {
        var map = CreateTestMap(9704);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_104, CreatureCbid, 1, 1);
        template.Location = new Vector4(41.5f, 12f, -88.25f, 0f);
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(41.5f, 12f, -88.25f);

        Assert.IsTrue(spawn.Spawn());
        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreEqual(41.5f, child.Position.X, 0.001f);
        Assert.AreEqual(-88.25f, child.Position.Z, 0.001f);
        Assert.AreNotEqual(0f, child.Position.X);
        Assert.AreNotEqual(0f, child.Position.Z);
    }

    [TestMethod]
    public void WalkingCreatureSpawn_IsMapped()
    {
        var map = CreateTestMap(9705);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_105, CreatureCbid, 1, 1));

        Assert.IsTrue(spawn.Spawn());
        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreSame(map, child.Map);
        Assert.IsTrue(map.Objects.ContainsKey(child.ObjectId));
    }

    [TestMethod]
    public void WalkingCreatureSpawn_HasGhostCreature()
    {
        var map = CreateTestMap(9706);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_106, CreatureCbid, 1, 1));

        Assert.IsTrue(spawn.Spawn());
        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.IsNotNull(child.Ghost);
        Assert.IsInstanceOfType(child.Ghost, typeof(GhostCreature));
    }

    [TestMethod]
    public void VehicleSpawn_CreatesVehicleAndDriver()
    {
        var map = CreateTestMap(9707);
        RegisterNpcVehicle();
        var spawn = PlaceSpawn(map, ActiveVehicleTemplate(20_107));

        Assert.IsTrue(spawn.Spawn());
        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.IsNotNull(vehicle.Owner, "NPC vehicle must have a driver");
        Assert.IsInstanceOfType(vehicle.Owner, typeof(Creature));
        Assert.AreEqual(DriverCbid, vehicle.Owner.CBID);
    }

    [TestMethod]
    public void VehicleSpawn_DriverBoundToVehicle()
    {
        var map = CreateTestMap(9708);
        RegisterNpcVehicle();
        var spawn = PlaceSpawn(map, ActiveVehicleTemplate(20_108));

        Assert.IsTrue(spawn.Spawn());
        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.IsNotNull(vehicle.Owner, "NPC vehicle must bind a driver");
        Assert.AreEqual(DriverCbid, vehicle.Owner.CBID);
        Assert.AreEqual(spawn.ObjectId.Coid, vehicle.SpawnOwnerCoid);
    }

    [TestMethod]
    public void VehicleSpawn_VehicleMappedDriverGhostless()
    {
        var map = CreateTestMap(9709);
        RegisterNpcVehicle();
        var spawn = PlaceSpawn(map, ActiveVehicleTemplate(20_109));

        Assert.IsTrue(spawn.Spawn());
        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        Assert.AreSame(map, vehicle.Map);
        Assert.IsNotNull(vehicle.Ghost);
        Assert.IsInstanceOfType(vehicle.Ghost, typeof(GhostVehicle));
        Assert.IsNull(vehicle.Owner!.Map, "production drivers stay unmapped");
        Assert.IsNull(vehicle.Owner.Ghost, "production drivers stay ghostless");
    }

    [TestMethod]
    public void FirstPlayerEntry_InitialSpawnsExistBeforeScope()
    {
        var map = CreateTestMap(9710);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_110, CreatureCbid, 2, 2);
        map.MapData.Templates[template.COID] = template;
        map.InitializeLocalObjectsForTests();

        Assert.AreEqual(2, CountMappedCreatures(map),
            "InitializeLocalObjects must materialize authored children before any player scopes");

        var player = new Character { Position = new Vector3(0f, 0f, 0f) };
        player.SetCoid(9001, true);
        player.SetMap(map);

        Assert.AreEqual(2, CountMappedCreatures(map),
            "first player enter must find destination children already mapped");
    }

    [TestMethod]
    public void InitialLogin_AndMapTransfer_UseSameSpawnInitialization()
    {
        RegisterWalkingCreature(CreatureCbid);
        var loginMap = CreateTestMap(9711);
        var transferMap = CreateTestMap(9712);
        var loginTpl = ActiveCreatureTemplate(20_111, CreatureCbid, 2, 2);
        var transferTpl = ActiveCreatureTemplate(20_112, CreatureCbid, 2, 2);
        loginMap.MapData.Templates[loginTpl.COID] = loginTpl;
        transferMap.MapData.Templates[transferTpl.COID] = transferTpl;

        loginMap.InitializeLocalObjectsForTests();
        transferMap.InitializeLocalObjectsForTests();

        Assert.AreEqual(CountMappedCreatures(loginMap), CountMappedCreatures(transferMap),
            "new SectorMap construction (login or transfer into a new map) must use the same InitializeLocalObjects");
        Assert.IsTrue(SectorMap.ShouldSpawnChildrenAtMapLoad(loginTpl));
        Assert.IsTrue(SectorMap.ShouldSpawnChildrenAtMapLoad(transferTpl));
    }

    [TestMethod]
    public void ScopeQuery_IncludesSpawnedWalkingCreature()
    {
        var map = CreateTestMap(9713);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_113, CreatureCbid, 1, 1));
        spawn.Position = new Vector3(10f, 0f, 10f);
        Assert.IsTrue(spawn.Spawn());
        var npc = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        npc.Position = new Vector3(10f, 0f, 10f);

        var selected = ScopeNearby(map, new Vector3(0f, 0f, 0f), npc);
        Assert.IsTrue(selected.Contains(npc), "a mapped walking NPC inside add radius must be scope-eligible");
    }

    [TestMethod]
    public void ScopeQuery_IncludesSpawnedNpcVehicle()
    {
        var map = CreateTestMap(9714);
        RegisterNpcVehicle();
        var spawn = PlaceSpawn(map, ActiveVehicleTemplate(20_114));
        spawn.Position = new Vector3(15f, 0f, 15f);
        Assert.IsTrue(spawn.Spawn());
        var vehicle = map.Objects.Values.OfType<Vehicle>().Single();
        vehicle.Position = new Vector3(15f, 0f, 15f);

        var selected = ScopeNearby(map, new Vector3(0f, 0f, 0f), vehicle);
        Assert.IsTrue(selected.Contains(vehicle), "a mapped NPC vehicle inside add radius must be scope-eligible");
    }

    [TestMethod]
    public void ScopeQuery_DistanceUsesSquaredXZ_Boundary()
    {
        var map = CreateTestMap(9715);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_115, CreatureCbid, 1, 1));
        Assert.IsTrue(spawn.Spawn());
        var npc = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);

        var add = InterestSelector.BaseScopeAddRadius;
        npc.Position = new Vector3(add - 1f, 500f, 0f);
        Assert.IsTrue(ScopeNearby(map, new Vector3(0f, 0f, 0f), npc).Contains(npc),
            "NPC just inside add radius (XZ, ignoring huge Y) must be eligible");

        npc.Position = new Vector3(add + 1f, 0f, 0f);
        Assert.IsFalse(ScopeNearby(map, new Vector3(0f, 0f, 0f), npc).Contains(npc),
            "NPC just outside add radius must not be a new scope candidate");
    }

    [TestMethod]
    public void SpawnPoint_CapacityMatchesRetailSemantics()
    {
        Assert.AreEqual(1, SpawnPointTemplate.ResolveSlotPopulationTarget(
            new SpawnPointTemplate.SpawnList { SpawnType = 1, LowerNumberOfSpawns = 0, UpperNumberOfSpawns = 0 },
            new Random(1)),
            "unauthored 0/0 defaults to one child");
        Assert.AreEqual(0, SpawnPointTemplate.ResolveSlotPopulationTarget(
            new SpawnPointTemplate.SpawnList { SpawnType = 1, LowerNumberOfSpawns = 1, UpperNumberOfSpawns = 0 },
            new Random(1)),
            "retail FUN_00566490 skips a slot whose Upper is 0 (and Lower was authored)");
        Assert.AreEqual(3, SpawnPointTemplate.ResolveSlotPopulationTarget(
            new SpawnPointTemplate.SpawnList { SpawnType = 1, LowerNumberOfSpawns = 3, UpperNumberOfSpawns = 3 },
            new Random(1)));
        Assert.AreEqual(10, SpawnPointTemplate.ResolveSlotPopulationTarget(
            new SpawnPointTemplate.SpawnList { SpawnType = 1, LowerNumberOfSpawns = 10, UpperNumberOfSpawns = 12 },
            new Random(1)),
            "retail caps Lower/Upper at 10");
        Assert.AreEqual(0, SpawnPointTemplate.ResolveSlotPopulationTarget(
            new SpawnPointTemplate.SpawnList { SpawnType = -1, LowerNumberOfSpawns = 4, UpperNumberOfSpawns = 8 },
            new Random(1)));
    }

    [TestMethod]
    public void SpawnPoint_HonorsPerSlotLowerUpper_AndAllFilledSlots()
    {
        var map = CreateTestMap(9716);
        RegisterWalkingCreature(CreatureCbid);
        RegisterWalkingCreature(OtherCreatureCbid);
        var template = new SpawnPointTemplate { COID = 20_116, OriginalIsActive = true, IsActive = true };
        template.Spawns.Add(FilledSlot(CreatureCbid, 2, 2));
        template.Spawns.Add(FilledSlot(OtherCreatureCbid, 1, 1));
        var spawn = PlaceSpawn(map, template);

        Assert.IsTrue(spawn.Spawn());
        var children = map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList();
        Assert.AreEqual(3, children.Count, "retail keeps both slot populations (2 + 1), not pick-one");
        Assert.AreEqual(2, children.Count(c => c.CBID == CreatureCbid));
        Assert.AreEqual(1, children.Count(c => c.CBID == OtherCreatureCbid));
    }

    [TestMethod]
    public void SpawnPoint_DeathSchedulesRespawn()
    {
        var map = CreateTestMap(9717);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_117, CreatureCbid, 1, 1);
        template.RespawnTime = 30_000f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        child.SetMap(null);
        spawn.NotifySpawnedChildDied(child, null);

        Assert.IsFalse(spawn.HasLiveSpawn());
        Assert.IsTrue(spawn.HasScheduledRespawn,
            "a positive retail RespawnTime must schedule a millisecond heartbeat after the last child dies");
    }

    [TestMethod]
    public void SpawnPoint_RespawnOccurs()
    {
        var map = CreateTestMap(9718);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_118, CreatureCbid, 1, 1);
        template.RespawnTime = 5_000f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        var first = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        var firstCoid = first.ObjectId.Coid;
        first.SetMap(null);
        spawn.NotifySpawnedChildDied(first, null);

        Assert.IsNotNull(spawn.RespawnDueAtMs);
        spawn.TickRespawn(spawn.RespawnDueAtMs!.Value);

        Assert.IsTrue(spawn.HasLiveSpawn());
        var replacement = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        Assert.AreNotEqual(firstCoid, replacement.ObjectId.Coid);
        Assert.AreSame(map, replacement.Map);
        Assert.IsNotNull(replacement.Ghost);
    }

    [TestMethod]
    public void SpawnPoint_RespawnTimerMatchesRetailUnits()
    {
        Assert.AreEqual(31_500, SpawnPoint.ComputeRespawnDelayMs(30_000f, jitterMs: 1_500),
            "RespawnTime is milliseconds plus 1000..2999 jitter, not seconds");
        Assert.AreEqual(120_000, SpawnPoint.ComputeRespawnDelayMs(195_000f, jitterMs: 1_000),
            "retail FUN_005635e0 clamps delays >= 119999 ms to 120000");
        Assert.AreEqual(1_000, SpawnPoint.ComputeRespawnDelayMs(0f, jitterMs: 1_000));
        Assert.IsNull(SpawnPoint.ComputeRespawnDelayMs(-1f, jitterMs: 1_500),
            "RespawnTime -1 means no heartbeat");
    }

    [TestMethod]
    public void SpawnPoint_NegativeRespawnTime_DoesNotRefillAfterDeath()
    {
        var map = CreateTestMap(9719);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_119, CreatureCbid, 1, 1);
        template.RespawnTime = -1f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        var child = map.Objects.Values.OfType<Creature>().Single(c => c is not Character);
        child.SetMap(null);
        spawn.NotifySpawnedChildDied(child, null);

        Assert.IsFalse(spawn.HasScheduledRespawn);
        spawn.TickRespawn(Environment.TickCount64 + 1_000_000);
        Assert.AreEqual(0, CountMappedCreatures(map));
    }

    [TestMethod]
    public void InactiveSpawn_ActivatesOnRequiredReaction()
    {
        var map = CreateTestMap(9720);
        RegisterWalkingCreature(CreatureCbid);
        var template = new SpawnPointTemplate
        {
            COID = 20_120,
            IsActive = false,
            OriginalIsActive = false,
        };
        template.Spawns.Add(FilledSlot(CreatureCbid, 2, 2));
        map.MapData.Templates[template.COID] = template;
        map.InitializeLocalObjectsForTests();

        var spawn = map.GetObjectByCoid(template.COID) as SpawnPoint;
        Assert.IsFalse(spawn!.HasLiveSpawn());

        var actTpl = new ReactionTemplate { COID = 20_121, ReactionType = ReactionType.Activate };
        actTpl.Objects.Add(template.COID);
        var activate = new Reaction(actTpl);
        activate.SetCoid(20_121, false);
        activate.SetMap(map);
        var player = new Character();
        player.SetCoid(9001, true);
        player.SetMap(map);

        Assert.IsTrue(activate.TriggerIfPossible(player));
        Assert.IsTrue(spawn.HasLiveSpawn());
        Assert.AreEqual(2, CountMappedCreatures(map),
            "Activate must honor authored Lower/Upper, not a single child");
        Assert.IsFalse(template.OriginalIsActive);
    }

    [TestMethod]
    public void MissionPhase_DestinationSpawnsApplied()
    {
        var map = CreateTestMap(9721);
        RegisterWalkingCreature(CreatureCbid);
        var inactive = new SpawnPointTemplate
        {
            COID = 20_122,
            IsActive = false,
            OriginalIsActive = false,
        };
        inactive.Spawns.Add(FilledSlot(CreatureCbid, 1, 1));
        map.MapData.Templates[inactive.COID] = inactive;
        map.InitializeLocalObjectsForTests();

        var spawn = map.GetObjectByCoid(inactive.COID) as SpawnPoint;
        Assert.IsFalse(spawn!.HasLiveSpawn(), "mission-phase object starts fam-inactive");

        var character = new Character();
        character.SetCoid(9101, true);
        character.SetMap(map);
        map.ApplyMissionPhaseWorldState(character);

        // ApplyMissionPhaseWorldState is the shared login+transfer hook. This pin requires
        // the call to be safe on transfer into a map whose inactive spawns are still markers.
        Assert.IsNotNull(map.GetObjectByCoid(inactive.COID));
        Assert.AreEqual(character.Map, map);
    }

    [TestMethod]
    public void RepeatedTransfer_DoesNotLoseSpawnChildren()
    {
        var map = CreateTestMap(9722);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_123, CreatureCbid, 2, 2);
        map.MapData.Templates[template.COID] = template;
        map.InitializeLocalObjectsForTests();

        var before = CountMappedCreatures(map);
        var player = new Character();
        player.SetCoid(9201, true);
        player.SetMap(map);
        player.SetMap(null);
        player.SetMap(map);
        player.SetMap(null);
        player.SetMap(map);

        Assert.AreEqual(before, CountMappedCreatures(map),
            "A→B→A on a persisted map must not drop already-materialized children");
        Assert.IsTrue((map.GetObjectByCoid(template.COID) as SpawnPoint)!.HasLiveSpawn());
    }

    [TestMethod]
    public void RepeatedTransfer_DoesNotDuplicateSpawnChildren()
    {
        var map = CreateTestMap(9723);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_124, CreatureCbid, 2, 2);
        map.MapData.Templates[template.COID] = template;
        map.InitializeLocalObjectsForTests();
        var before = CountMappedCreatures(map);

        map.ApplyAuthoredSpawnHygiene();
        map.ApplyAuthoredSpawnHygiene();

        Assert.AreEqual(before, CountMappedCreatures(map),
            "re-enter hygiene must refill deficits, not duplicate a full camp");
    }

    [TestMethod]
    public void CachedMap_RefillsDeadChildrenOnHeartbeat()
    {
        var map = CreateTestMap(9724);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_125, CreatureCbid, 2, 2);
        template.RespawnTime = 1_000f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        foreach (var child in map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList())
        {
            child.SetMap(null);
            spawn.NotifySpawnedChildDied(child, null);
        }

        Assert.AreEqual(0, CountMappedCreatures(map));
        map.TickSpawnRespawns(spawn.RespawnDueAtMs ?? 0);
        Assert.AreEqual(2, CountMappedCreatures(map),
            "a persisted map must refill from the spawn heartbeat even after every child died");
    }

    /// <summary>
    /// A SpawnPoint detached from the map (local-world reset / instance disposal calls
    /// <c>SetMap(null)</c> on every non-Character object) must not stay on the respawn
    /// heartbeat list. Otherwise the next due tick runs <c>SpawnCreature</c> with
    /// <c>Map == null</c> and NREs on <c>Map.LocalCoidCounter</c>, every tick, forever.
    /// </summary>
    [TestMethod]
    public void DetachedSpawnPoint_IsRemovedFromRespawnHeartbeat()
    {
        var map = CreateTestMap(9726);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_127, CreatureCbid, 2, 2);
        template.RespawnTime = 1_000f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        foreach (var child in map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList())
        {
            child.SetMap(null);
            spawn.NotifySpawnedChildDied(child, null);
        }

        var dueAt = spawn.RespawnDueAtMs ?? 0;
        Assert.IsTrue(dueAt > 0, "child deaths must schedule a refill heartbeat");

        // Local-world reset: detach the spawn point itself.
        spawn.SetMap(null);

        map.TickSpawnRespawns(dueAt);

        Assert.AreEqual(0, CountMappedCreatures(map),
            "a detached spawn point must not refill a map it no longer belongs to");
    }

    /// <summary>
    /// Teardown clears every other map-local collection; the respawn heartbeat must go too.
    /// LeaveMap's unregister is skipped when the object was already dropped from
    /// <c>Objects</c> (documented PlayerCount-drift path), so the list must also be cleared here.
    /// </summary>
    [TestMethod]
    public void TearDownLocalEntities_ClearsRespawnHeartbeat()
    {
        var map = CreateTestMap(9727);
        RegisterWalkingCreature(CreatureCbid);
        var template = ActiveCreatureTemplate(20_128, CreatureCbid, 2, 2);
        template.RespawnTime = 1_000f;
        var spawn = PlaceSpawn(map, template);
        Assert.IsTrue(spawn.Spawn());

        foreach (var child in map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList())
        {
            child.SetMap(null);
            spawn.NotifySpawnedChildDied(child, null);
        }

        var dueAt = spawn.RespawnDueAtMs ?? 0;
        Assert.IsTrue(dueAt > 0, "child deaths must schedule a refill heartbeat");

        // PlayerCount drift: the spawn point is gone from Objects, so LeaveMap will early-return.
        map.Objects.Remove(spawn.ObjectId);

        map.TearDownLocalEntities();
        map.TickSpawnRespawns(dueAt);

        Assert.AreEqual(0, CountMappedCreatures(map),
            "teardown must drop the respawn heartbeat along with the other map-local collections");
    }

    [TestMethod]
    public void UnsupportedSpawnChild_ProducesDiagnostic()
    {
        var map = CreateTestMap(9725);
        AssetManagerTestHelper.RegisterCloneBase(CreatureCbid, CloneBaseObjectType.Object);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_126, CreatureCbid, 1, 1));

        Assert.IsFalse(spawn.Spawn());
        StringAssert.Contains(spawn.LastFailureDiagnostic ?? string.Empty, "clone type");
        StringAssert.Contains(spawn.LastFailureDiagnostic ?? string.Empty, CreatureCbid.ToString());
        StringAssert.Contains(spawn.LastFailureDiagnostic ?? string.Empty, "20");
    }

    [TestMethod]
    public void MissingChildTemplate_ProducesDiagnostic()
    {
        var map = CreateTestMap(9726);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_127, 1_999_999, 1, 1));

        Assert.IsFalse(spawn.Spawn());
        StringAssert.Contains(spawn.LastFailureDiagnostic ?? string.Empty, "template");
        StringAssert.Contains(spawn.LastFailureDiagnostic ?? string.Empty, "1999999");
    }

    [TestMethod]
    public void EmptySpawnSlots_DiagnosticNamesMapPositionAndWhySlotsAreEmpty()
    {
        var map = CreateTestMap(9729);
        var template = new SpawnPointTemplate
        {
            COID = 99112,
            OriginalIsActive = true,
            IsActive = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1 });
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = 4401,
            IsTemplate = false,
            LowerNumberOfSpawns = 2,
            UpperNumberOfSpawns = 0,
        });
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(12.5f, 3f, -40f);

        Assert.IsFalse(spawn.Spawn());
        var diag = spawn.LastFailureDiagnostic ?? string.Empty;
        StringAssert.Contains(diag, "99112");
        StringAssert.Contains(diag, "9729");
        StringAssert.Contains(diag, "tm_dest_spawn_9729");
        StringAssert.Contains(diag, "4401");
        StringAssert.Contains(diag, "pos=");
        StringAssert.Contains(diag, "12.5");
        StringAssert.Contains(diag, "-40");
        StringAssert.Contains(diag, "Upper=0");
        StringAssert.Contains(diag, "emptyType=");
    }

    [TestMethod]
    public void DescribeUnfilledSlots_NoList_SaysNoSpawnList()
    {
        var template = new SpawnPointTemplate();
        StringAssert.Contains(template.DescribeUnfilledSlots(), "no spawn list");
        StringAssert.Contains(template.DescribeUnfilledSlots(), "slots=0");
    }

    [TestMethod]
    public void DescribeUnfilledSlots_FilledTemplate_StillReportsExpectedMinimumZeroFallback()
    {
        var template = new SpawnPointTemplate();
        template.Spawns.Add(FilledSlot(4401, 1, 1));
        StringAssert.Contains(template.DescribeUnfilledSlots(), "expected minimum is 0");
        StringAssert.Contains(template.DescribeUnfilledSlots(), "type=4401");
    }

    [TestMethod]
    public void EmptySpawnSlots_AllUnset_DiagnosticSaysSpawnTypeUnset()
    {
        var map = CreateTestMap(9730);
        var template = new SpawnPointTemplate
        {
            COID = 99120,
            OriginalIsActive = true,
            IsActive = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1 });
        template.Spawns.Add(new SpawnPointTemplate.SpawnList { SpawnType = -1 });
        var spawn = PlaceSpawn(map, template);
        spawn.Position = new Vector3(1f, 2f, 3f);

        Assert.IsFalse(spawn.Spawn());
        var diag = spawn.LastFailureDiagnostic ?? string.Empty;
        StringAssert.Contains(diag, "99120");
        StringAssert.Contains(diag, "9730");
        StringAssert.Contains(diag, "SpawnType=-1");
        StringAssert.Contains(diag, "slots=2");
    }

    [TestMethod]
    public void RealFam_InitiallyActiveSpawnPointsCreateExpectedChildren()
    {
        var map = CreateTestMap(9727);
        RegisterWalkingCreature(CreatureCbid);
        var pike = new SpawnPointTemplate
        {
            COID = 12636,
            OriginalIsActive = true,
            IsActive = true,
            RespawnTime = 195000f,
        };
        pike.Spawns.Add(FilledSlot(CreatureCbid, 2, 2));
        var brood = new SpawnPointTemplate
        {
            COID = 13330,
            OriginalIsActive = true,
            IsActive = true,
        };
        brood.Spawns.Add(FilledSlot(CreatureCbid, 10, 10));
        brood.Spawns.Add(FilledSlot(OtherCreatureCbid, 2, 2));
        RegisterWalkingCreature(OtherCreatureCbid);
        map.MapData.Templates[pike.COID] = pike;
        map.MapData.Templates[brood.COID] = brood;

        map.InitializeLocalObjectsForTests();

        Assert.AreEqual(2, CountOwned(map, 12636), "Scrap pike camp Lower=2 must not collapse to 1");
        Assert.AreEqual(12, CountOwned(map, 13330), "Scrap brood camp 10+2 must not collapse to 1");
    }

    [TestMethod]
    public void HasLiveSpawn_TrueWhileAnyOwnedChildRemains()
    {
        var map = CreateTestMap(9728);
        RegisterWalkingCreature(CreatureCbid);
        var spawn = PlaceSpawn(map, ActiveCreatureTemplate(20_128, CreatureCbid, 2, 2));
        Assert.IsTrue(spawn.Spawn());

        var children = map.Objects.Values.OfType<Creature>().Where(c => c is not Character).ToList();
        Assert.AreEqual(2, children.Count);
        children[0].SetMap(null);
        Assert.IsTrue(spawn.HasLiveSpawn(), "one surviving sibling still counts as a live spawn");
    }

    private static int CountOwned(SectorMap map, long spawnCoid)
        => map.Objects.Values.Count(o =>
            (o is Creature c && c is not Character && c.SpawnOwner == spawnCoid)
            || (o is Vehicle v && v.SpawnOwnerCoid == spawnCoid));

    private static int CountMappedCreatures(SectorMap map)
        => map.Objects.Values.OfType<Creature>().Count(c => c is not Character);

    private static List<ClonedObjectBase> ScopeNearby(SectorMap map, Vector3 center, ClonedObjectBase entity)
    {
        var output = new List<ClonedObjectBase>();
        InterestSelector.Select(
            self: null,
            center,
            isTown: false,
            players: Array.Empty<ClonedObjectBase>(),
            missionGivers: Array.Empty<ClonedObjectBase>(),
            nearby: new[] { entity },
            isGhosted: _ => false,
            output);
        return output;
    }

    private static void RegisterWalkingCreature(int cbid)
        => AssetManagerTestHelper.RegisterCreatureCloneBase(cbid, isNpc: 0);

    private static void RegisterNpcVehicle()
    {
        AssetManagerTestHelper.RegisterCloneBase(WheelsetCbid, CloneBaseObjectType.WheelSet);
        AssetManagerTestHelper.RegisterVehicleCloneBase(VehicleCbid, defaultDriverCbid: DriverCbid, defaultWheelsetCbid: WheelsetCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(DriverCbid, isNpc: 0);
        AssetManager.Instance.SetTestVehicleTemplates(new[]
        {
            new VehicleTemplate
            {
                Id = TemplateId,
                VehicleCbid = VehicleCbid,
                DriverCbid = DriverCbid,
            }
        });
    }

    private static SpawnPointTemplate ActiveCreatureTemplate(int coid, int cbid, byte lower, byte upper)
    {
        var template = new SpawnPointTemplate
        {
            COID = coid,
            OriginalIsActive = true,
            IsActive = true,
        };
        template.Spawns.Add(FilledSlot(cbid, lower, upper));
        return template;
    }

    private static SpawnPointTemplate ActiveVehicleTemplate(int coid)
    {
        var template = new SpawnPointTemplate
        {
            COID = coid,
            OriginalIsActive = true,
            IsActive = true,
        };
        template.Spawns.Add(new SpawnPointTemplate.SpawnList
        {
            SpawnType = TemplateId,
            IsTemplate = true,
            LowerNumberOfSpawns = 1,
            UpperNumberOfSpawns = 1,
        });
        return template;
    }

    private static SpawnPointTemplate.SpawnList FilledSlot(int spawnType, byte lower, byte upper)
        => new()
        {
            SpawnType = spawnType,
            IsTemplate = false,
            LowerNumberOfSpawns = lower,
            UpperNumberOfSpawns = upper,
        };

    private static SpawnPoint PlaceSpawn(SectorMap map, SpawnPointTemplate template)
    {
        var spawnPoint = (SpawnPoint)template.Create();
        spawnPoint.SetCoid(template.COID, false);
        spawnPoint.SetMap(map);
        return spawnPoint;
    }

    private static SectorMap CreateTestMap(int continentId)
    {
        var continent = new ContinentObject
        {
            Id = continentId,
            MapFileName = $"tm_dest_spawn_{continentId}",
            DisplayName = "destination-spawn",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
