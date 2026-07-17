using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Combat;

using AutoCore.Database.World.Models;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Combat;
using AutoCore.Game.Constants;
using AutoCore.Game.Diagnostics;
// MapPropCorpseDespawn
using AutoCore.Game.Entities;
using AutoCore.Game.Managers;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

[TestClass]
public class VehicleMapPropRamTests
{
    private readonly List<BasePacket> _sent = new();

    [TestInitialize]
    public void SetUp()
    {
        _sent.Clear();
        TNLConnection.TestPacketSink = (_, p) => _sent.Add(p);
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        VehicleMapPropRam.ResetCooldownsForTests();
        MapPropCorpseDespawn.ResetForTests();
        // Production default is off; unit tests exercise the feature when enabled.
        ServerConfig.EnableRamming = true;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        LootManager.Instance.ResetForTests();
        VehicleMapPropRam.ResetCooldownsForTests();
        MapPropCorpseDespawn.ResetForTests();
        ServerConfig.ResetToDefaults();
    }

    [TestMethod]
    public void SoftCollidableProp_AtSpeed_IsDestroyedAndLeavesMap()
    {
        const int propCbid = 9901;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var prop = CreatePropOnMap(map, coid: 88001, cbid: propCbid, maxHp: 20, position: vehicle.Position);

        var hits = VehicleMapPropRam.Process(vehicle);

        Assert.IsTrue(hits >= 1);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsNotNull(map.GetObjectByCoid(88001), "corpse stays on map until delayed despawn");
        Assert.AreEqual(1, MapPropCorpseDespawn.PendingCountForTests);
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(88001), "after delay, prop leaves the map");
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == 88001));
    }

    [TestMethod]
    public void Process_WhenEnableRammingFalse_DoesNotDamageProp()
    {
        ServerConfig.EnableRamming = false;

        const int propCbid = 9900;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var prop = CreatePropOnMap(map, coid: 88000, cbid: propCbid, maxHp: 20, position: vehicle.Position);

        Assert.AreEqual(0, VehicleMapPropRam.Process(vehicle));
        Assert.AreEqual(20, prop.GetCurrentHP());
        Assert.IsFalse(prop.IsCorpse);
    }

    [TestMethod]
    public void InvincibleProp_IsNotDamaged()
    {
        const int propCbid = 9902;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 50, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 25f);
        var prop = CreatePropOnMap(map, coid: 88002, cbid: propCbid, maxHp: 50, position: vehicle.Position);
        prop.SetInvincible(true);

        var hits = VehicleMapPropRam.Process(vehicle);

        Assert.AreEqual(0, hits);
        Assert.AreEqual(50, prop.GetCurrentHP());
        Assert.IsFalse(prop.IsCorpse);
    }

    [TestMethod]
    public void SlowVehicle_DoesNotRam()
    {
        const int propCbid = 9903;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 1f);
        var prop = CreatePropOnMap(map, coid: 88003, cbid: propCbid, maxHp: 20, position: vehicle.Position);

        Assert.AreEqual(0, VehicleMapPropRam.Process(vehicle));
        Assert.AreEqual(20, prop.GetCurrentHP());
    }

    [TestMethod]
    public void FarProp_IsNotHit()
    {
        const int propCbid = 9904;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        var far = new Vector3(vehicle.Position.X + 100, 0, vehicle.Position.Z + 100);
        var prop = CreatePropOnMap(map, coid: 88004, cbid: propCbid, maxHp: 20, position: far);

        Assert.AreEqual(0, VehicleMapPropRam.Process(vehicle));
        Assert.AreEqual(20, prop.GetCurrentHP());
    }

    [TestMethod]
    public void Process_SpatialQuery_ExcludesDistantPropsFromCandidates()
    {
        // Full-map scan used to count ~thousands eligibleProps; spatial query only nearby cells.
        const int propCbid = 9911;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 10, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        vehicle.Position = new Vector3(0, 0, 0);
        // Far props in other cells (well beyond ContactRadius 10 and cell neighborhood for R=10).
        for (var i = 0; i < 20; i++)
            CreatePropOnMap(map, coid: 88200 + i, cbid: propCbid, maxHp: 10,
                position: new Vector3(500 + i * 10, 0, 500));

        var near = CreatePropOnMap(map, coid: 88299, cbid: propCbid, maxHp: 10,
            position: new Vector3(2, 0, 0));

        var hits = VehicleMapPropRam.Process(vehicle);
        Assert.IsTrue(hits >= 1);
        Assert.IsTrue(near.IsCorpse);
        Assert.IsTrue(VehicleMapPropRam.LastSpatialCandidateCount < 20,
            $"spatial candidates should be local, got {VehicleMapPropRam.LastSpatialCandidateCount}");
        Assert.IsTrue(VehicleMapPropRam.LastNearbyEligibleCount >= 1);
    }

    [TestMethod]
    public void NonCollidableProp_IsSkipped()
    {
        const int propCbid = 9905;
        // Type Object with Flags collidable bit clear.
        RegisterObjectProp(propCbid, minHp: 1, maxHp: 30, collidable: false);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        var prop = CreatePropOnMap(map, coid: 88005, cbid: propCbid, maxHp: 30, position: vehicle.Position);

        Assert.AreEqual(0, VehicleMapPropRam.Process(vehicle));
        Assert.AreEqual(30, prop.GetCurrentHP());
    }

    [TestMethod]
    public void IsRamEligible_RejectsVehiclesAndCreatures()
    {
        var vehicle = new Vehicle();
        vehicle.InitializeHealthForTests(100);
        Assert.IsFalse(VehicleMapPropRam.IsRamEligibleMapProp(vehicle));

        var creature = new Creature();
        Assert.IsFalse(VehicleMapPropRam.IsRamEligibleMapProp(creature));
    }

    [TestMethod]
    public void SoftDestructible_TrueWhenMinHpBelow5()
    {
        const int propCbid = 9906;
        RegisterPhysicsProp(propCbid, minHp: 2, maxHp: 100, collidable: true);
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.LoadCloneBase(propCbid);
        prop.InitializeHealthForTests(100);

        Assert.IsTrue(VehicleMapPropRam.IsSoftDestructible(prop));
        Assert.AreEqual(100, VehicleMapPropRam.ComputeDamage(prop, speed: 15f));
    }

    [TestMethod]
    public void ResolveSpeed_UsesPositionDeltaWhenVelocityLow()
    {
        var vehicle = new Vehicle();
        vehicle.SetVelocityForTests(new Vector3(0.1f, 0, 0));
        vehicle.Position = new Vector3(20, 0, 0);
        var prev = new Vector3(0, 0, 0);
        // 20 units in 0.05s = 400 u/s
        var speed = VehicleMapPropRam.ResolveSpeed(vehicle, prev, dtSeconds: 0.05f);
        Assert.IsTrue(speed > 100f, $"expected position-derived speed, got {speed}");
    }

    [TestMethod]
    public void ZeroPacketVelocity_ButMoved_StillRamsSoftProp()
    {
        const int propCbid = 9907;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 15, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 0f);
        var prev = vehicle.Position;
        // Simulate client sending zero velocity while translating through a prop.
        vehicle.Position = new Vector3(prev.X + 8f, prev.Y, prev.Z);
        var prop = CreatePropOnMap(map, coid: 88007, cbid: propCbid, maxHp: 15, position: vehicle.Position);

        var hits = VehicleMapPropRam.Process(vehicle, previousPosition: prev, dtSeconds: 0.05f);
        Assert.IsTrue(hits >= 1, "position delta must drive ram when velocity packet is ~0");
        Assert.IsTrue(prop.IsCorpse);
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(88007));
    }

    [TestMethod]
    public void CorpseDespawn_DoesNotFireBeforeDelay_FiresAfter()
    {
        const int propCbid = 9908;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 20f);
        var prop = CreatePropOnMap(map, coid: 88008, cbid: propCbid, maxHp: 20, position: vehicle.Position);

        Assert.IsTrue(VehicleMapPropRam.Process(vehicle) >= 1);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsNotNull(map.GetObjectByCoid(88008));

        // 1s later — still scheduled, not finalized.
        var early = Environment.TickCount64 + 1_000;
        Assert.AreEqual(0, MapPropCorpseDespawn.Tick(early));
        Assert.IsNotNull(map.GetObjectByCoid(88008), "prop must remain ~12.5s after ram");
        Assert.AreEqual(1, MapPropCorpseDespawn.PendingCountForTests);

        // Past the default delay.
        var late = Environment.TickCount64 + MapPropCorpseDespawn.DespawnDelayMs + 500;
        Assert.AreEqual(1, MapPropCorpseDespawn.Tick(late));
        Assert.IsNull(map.GetObjectByCoid(88008), "prop leaves only after delay");
        Assert.IsTrue(_sent.OfType<DestroyObjectPacket>().Any(p => p.ObjectId.Coid == 88008));
    }

    [TestMethod]
    public void Process_HitsOnlyClosestProp_NotWholeCluster()
    {
        const int propCbid = 9910;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 5, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        vehicle.Position = new Vector3(0, 0, 0);
        // Cluster within old 14u radius — only the nearest should die per Process.
        var near = CreatePropOnMap(map, coid: 88100, cbid: propCbid, maxHp: 5, position: new Vector3(3, 0, 0));
        var mid = CreatePropOnMap(map, coid: 88101, cbid: propCbid, maxHp: 5, position: new Vector3(8, 0, 0));
        var far = CreatePropOnMap(map, coid: 88102, cbid: propCbid, maxHp: 5, position: new Vector3(12, 0, 0));

        var hits = VehicleMapPropRam.Process(vehicle);
        Assert.AreEqual(1, hits, "one contact / one prop per movement packet");
        Assert.IsTrue(near.IsCorpse, "closest prop dies");
        Assert.IsFalse(mid.IsCorpse, "mid cluster member must not AOE-die");
        Assert.IsFalse(far.IsCorpse, "far cluster member must not AOE-die");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    [TestMethod]
    public void Process_DoesNotCreateGhostOnKilledProp()
    {
        const int propCbid = 9911;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 10, collidable: true);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        var prop = CreatePropOnMap(map, coid: 88110, cbid: propCbid, maxHp: 10, position: vehicle.Position);

        Assert.IsTrue(VehicleMapPropRam.Process(vehicle) >= 1);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsNull(prop.Ghost, "ram must not scope plain GhostObject (client AV 0x005B0EFF)");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    [TestMethod]
    public void ResolveRamLootPosition_OffsetsAlongVelocity()
    {
        var vehicle = new Vehicle();
        vehicle.Position = new Vector3(100, 5, 200);
        // Moving +Z (forward along world Z).
        vehicle.SetVelocityForTests(new Vector3(0, 0, 20));
        vehicle.Rotation = Quaternion.Default;

        var lootPos = VehicleMapPropRam.ResolveRamLootPosition(vehicle);
        Assert.AreEqual(100f, lootPos.X, 0.05f);
        Assert.AreEqual(5f, lootPos.Y, 0.05f);
        Assert.AreEqual(200f + VehicleMapPropRam.LootForwardOffsetMeters, lootPos.Z, 0.1f);
    }

    [TestMethod]
    public void SoftCollidableProp_LootSpawnsInFrontOfVehicle_WhenWeighted()
    {
        const int propCbid = 9909;
        const int junkCbid = 2580;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 20, collidable: true);
        AssetManagerTestHelper.RegisterCloneBase(junkCbid, CloneBaseObjectType.Item);
        LootManager.Instance.SeedGeneratableItemForTests(CloneBaseObjectType.Item, 0, junkCbid, 1);
        AssetManager.Instance.SetTestLootWeights(new[]
        {
            new LootWeight { DestroyedCbid = propCbid, LootCbid = junkCbid, Weight = 500 },
        });

        var (vehicle, map) = CreateVehicleOnMap(speed: 25f);
        // CreateVehicleOnMap sets velocity +X at speed.
        vehicle.Position = new Vector3(777, 12, 333);
        var expected = VehicleMapPropRam.ResolveRamLootPosition(vehicle);
        var prop = CreatePropOnMap(map, coid: 88009, cbid: propCbid, maxHp: 20, position: vehicle.Position);
        prop.Position = vehicle.Position;

        Assert.IsTrue(VehicleMapPropRam.Process(vehicle) >= 1);
        Assert.IsTrue(prop.IsCorpse);

        var loot = map.Objects.Values.OfType<SimpleObject>().FirstOrDefault(o => o.CBID == junkCbid);
        Assert.IsNotNull(loot, "weighted map prop ram drops fixed junk only");
        Assert.IsTrue(
            loot.Position.Dist(expected) < 1.5f,
            $"loot at {loot.Position} should be ~{VehicleMapPropRam.LootForwardOffsetMeters}m in front (expected {expected})");
        Assert.IsTrue(
            loot.Position.Dist(vehicle.Position) > 2f,
            "loot must not spawn under the vehicle body");

        Assert.IsNotNull(map.GetObjectByCoid(88009));
        MapPropCorpseDespawn.FlushAllForTests();
        Assert.IsNull(map.GetObjectByCoid(88009));
    }

    [TestMethod]
    public void SoftCollidableProp_NoLootWhenNoLootWeights()
    {
        const int propCbid = 9912;
        const int salvageCbid = 5468;
        RegisterPhysicsProp(propCbid, minHp: 1, maxHp: 5, collidable: true);
        AssetManagerTestHelper.RegisterCloneBase(salvageCbid, CloneBaseObjectType.Commodity);
        LootManager.Instance.SeedCommodityForTests(salvageCbid, minLevel: 1, maxLevel: 100, dropChance: 1f);

        var (vehicle, map) = CreateVehicleOnMap(speed: 30f);
        var prop = CreatePropOnMap(map, coid: 88012, cbid: propCbid, maxHp: 5, position: vehicle.Position);

        Assert.IsTrue(VehicleMapPropRam.Process(vehicle) >= 1);
        Assert.IsTrue(prop.IsCorpse);
        Assert.IsFalse(
            map.Objects.Values.OfType<SimpleObject>().Any(o => o.CBID == salvageCbid),
            "rubble/fence without tLootWeights must not drop commodity salvage");
        MapPropCorpseDespawn.FlushAllForTests();
    }

    private static void RegisterPhysicsProp(int cbid, short minHp, short maxHp, bool collidable)
    {
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.ObjectGraphicsPhysics);
        var cb = (AutoCore.Game.CloneBases.CloneBaseObject)AssetManager.Instance.GetCloneBase(cbid)!;
        cb.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MinHitPoints = minHp,
            MaxHitPoint = maxHp,
            Flags = (short)(collidable ? 1 : 0),
        };
    }

    private static void RegisterObjectProp(int cbid, short minHp, short maxHp, bool collidable)
    {
        AssetManagerTestHelper.RegisterCloneBase(cbid, CloneBaseObjectType.Object);
        var cb = (AutoCore.Game.CloneBases.CloneBaseObject)AssetManager.Instance.GetCloneBase(cbid)!;
        cb.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            MinHitPoints = minHp,
            MaxHitPoint = maxHp,
            Flags = (short)(collidable ? 1 : 0),
        };
    }

    private static GraphicsObject CreatePropOnMap(SectorMap map, long coid, int cbid, int maxHp, Vector3 position)
    {
        var prop = new GraphicsObject(GraphicsObjectType.GraphicsPhysics);
        prop.SetCoid(coid, false);
        prop.LoadCloneBase(cbid);
        prop.InitializeHealthForTests(maxHp);
        prop.Position = position;
        prop.SetInvincible(false);
        prop.SetMap(map);
        return prop;
    }

    private static (Vehicle Vehicle, SectorMap Map) CreateVehicleOnMap(float speed)
    {
        var continent = new ContinentObject
        {
            Id = 811,
            MapFileName = "tm_prop_ram",
            DisplayName = "ram",
            DropCommodities = true,
        };
        var map = SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.SetGhostTo(false);

        var character = new Character();
        character.SetCoid(7001, true);
        character.SetOwningConnection(connection);
        connection.CurrentCharacter = character;

        var vehicle = new Vehicle();
        vehicle.SetCoid(7002, true);
        vehicle.InitializeHealthForTests(500);
        vehicle.Position = new Vector3(10, 0, 10);
        vehicle.SetVelocityForTests(new Vector3(speed, 0, 0));
        character.SetCurrentVehicleForTests(vehicle);

        character.SetMap(map);
        vehicle.SetMap(map);
        return (vehicle, map);
    }
}
