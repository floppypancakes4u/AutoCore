using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Map;

using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;

/// <summary>
/// Target-frame Cur/Max needs client vehicle+0xAC via SetVehicle.
/// Client PostCreate (CVOGCreature_PostCreateFromPacket) only calls SetVehicle when the vehicle
/// already exists and packet+0xF8 is set — so CreateVehicle must precede CreateCreature.
/// FUN_0080af70 no-ops when the driver TFID already exists, so attach reapply must destroy the
/// driver before recreating it (otherwise PostCreate never runs again).
/// See NPC.md §14.4.
/// </summary>
[TestClass]
public class ForeignDriverCreateScopeTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        SectorMap.ScopeGlobalVehicles = true;
        SectorMap.ScopeGlobalVehicleCreate = true;
        SectorMap.ScopeGlobalVehicleGhost = true;
        TNLConnection.ForeignGhostScopeHoldQueries = 2;
        TNLConnection.ForeignGhostScopeHoldMilliseconds = 0;
        TNLConnection.ForeignCreateHoldStaleGraceMilliseconds = 0;
        TNLConnection.ForceForeignCreateReapply = false;
        TNLConnection.ForeignOwnerAttachReapplyMilliseconds = 50;
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
        TNLConnection.ResetForeignGhostHoldDefaultsForTests();
    }

    [TestMethod]
    public void PerformScopeQuery_NpcWithDriver_SendsCreateVehicleThenCreateCreature()
    {
        const int vehicleCbid = 650_201;
        const int driverCbid = 650_202;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 50_201;
        const long driverCoid = MapNpcIdentity.CoidBase + 50_202;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 80);

        var map = CreateFieldMap();
        var driver = new Creature { Position = new Vector3(25f, 0f, 0f), Level = 5 };
        driver.SetCoid(driverCoid, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var npcVehicle = new Vehicle { Position = new Vector3(25f, 0f, 0f) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetOwner(driver);
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);

        var driverCreate = packets.OfType<CreateCreaturePacket>().SingleOrDefault();
        Assert.IsNotNull(driverCreate,
            "NPC driver must be CreateCreature'd with vehicle link for PostCreate SetVehicle");
        Assert.AreEqual(driverCoid, driverCreate.ObjectId.Coid);
        Assert.AreEqual(driverCbid, driverCreate.CBID);
        Assert.AreEqual(vehicleCoid, driverCreate.CoidCurrentVehicle,
            "packet+0xF8 must be chassis COID so CVOGCreature_PostCreateFromPacket calls SetVehicle");
        Assert.AreEqual((byte)5, driverCreate.Level);

        var vehicleCreate = packets.OfType<CreateVehiclePacket>().SingleOrDefault();
        Assert.IsNotNull(vehicleCreate);
        Assert.AreEqual(vehicleCoid, vehicleCreate.ObjectId.Coid);
        Assert.AreEqual(driverCoid, vehicleCreate.CoidCurrentOwner);

        var vehicleIdx = packets.FindIndex(p => p is CreateVehiclePacket);
        var driverIdx = packets.FindIndex(p => p is CreateCreaturePacket);
        Assert.IsTrue(vehicleIdx >= 0 && driverIdx > vehicleIdx,
            "CreateVehicle must precede CreateCreature so PostCreate can resolve the chassis");
    }

    [TestMethod]
    public void PerformScopeQuery_NpcWithoutDriver_NoCreateCreature()
    {
        const int vehicleCbid = 650_203;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 50_203;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var map = CreateFieldMap();
        var npcVehicle = new Vehicle { Position = new Vector3(10f, 0f, 0f) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);

        Assert.AreEqual(0, packets.OfType<CreateCreaturePacket>().Count());
        Assert.IsNotNull(packets.OfType<CreateVehiclePacket>().SingleOrDefault());
    }

    [TestMethod]
    public void PerformScopeQuery_OwnerAttachReapply_RecreatesDriverAfterVehicle()
    {
        const int vehicleCbid = 650_204;
        const int driverCbid = 650_205;
        const long vehicleCoid = MapNpcIdentity.CoidBase + 50_204;
        const long driverCoid = MapNpcIdentity.CoidBase + 50_205;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid);

        var map = CreateFieldMap();
        var driver = new Creature { Position = new Vector3(12f, 0f, 0f), Level = 3 };
        driver.SetCoid(driverCoid, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var npcVehicle = new Vehicle { Position = new Vector3(12f, 0f, 0f) };
        npcVehicle.SetCoid(vehicleCoid, true);
        npcVehicle.LoadCloneBase(vehicleCbid);
        npcVehicle.SetupCBFields();
        npcVehicle.SetOwner(driver);
        npcVehicle.SetMap(map);
        npcVehicle.CreateGhost();

        var self = new Character { Position = new Vector3(0f, 0f, 0f) };
        self.SetCurrentVehicleForTests(new Vehicle { Position = self.Position });
        var connection = new TNLConnection();
        connection.SetGhostFrom(true);
        connection.ActivateGhosting();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, packet) => packets.Add(packet);

        map.PerformScopeQuery(null, self, connection);
        map.PerformScopeQuery(null, self, connection);
        map.PerformScopeQuery(null, self, connection);

        connection.DebugAgeForeignOwnerAttachReapplyForTests(vehicleCoid, 200);
        var before = packets.Count;
        map.PerformScopeQuery(null, self, connection);

        var after = packets.Skip(before).ToList();
        Assert.IsTrue(after.OfType<CreateCreaturePacket>().Any());
        Assert.IsTrue(after.OfType<CreateVehiclePacket>().Any());

        // Must destroy both chassis and driver so CreateCreature is a full create (not TFID no-op).
        var destroys = after.OfType<DestroyObjectPacket>().ToList();
        Assert.IsTrue(destroys.Any(d => d.ObjectId.Coid == vehicleCoid), "destroy chassis");
        Assert.IsTrue(destroys.Any(d => d.ObjectId.Coid == driverCoid),
            "destroy driver so next CreateCreature runs PostCreate SetVehicle");

        // Order: destroy vehicle → destroy driver → CreateVehicle → CreateCreature
        var destroyVehIdx = after.FindIndex(p =>
            p is DestroyObjectPacket d && d.ObjectId.Coid == vehicleCoid);
        var destroyDrvIdx = after.FindIndex(p =>
            p is DestroyObjectPacket d && d.ObjectId.Coid == driverCoid);
        var createVehIdx = after.FindIndex(p => p is CreateVehiclePacket);
        var createDrvIdx = after.FindIndex(p => p is CreateCreaturePacket);
        Assert.IsTrue(destroyVehIdx >= 0 && destroyDrvIdx > destroyVehIdx
            && createVehIdx > destroyDrvIdx && createDrvIdx > createVehIdx,
            "Order: Destroy(vehicle) → Destroy(driver) → CreateVehicle → CreateCreature");

        var driverPkt = after.OfType<CreateCreaturePacket>().Last();
        Assert.AreEqual(vehicleCoid, driverPkt.CoidCurrentVehicle);
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new Database.World.Models.ContinentObject
        {
            Id = 708,
            MapFileName = "sec_f_h_map_tut_j2_arkbaytutorial",
            DisplayName = "Hestia Ark Bay DriverCreate",
            IsTown = false,
        };

        var map = SectorMap.CreateForTests(continent, new Vector4(0f, 0f, 0f, 0f));
        foreach (var fieldName in new[] { "_scopeNearby", "_scopeMissionGivers", "_scopeSelected" })
        {
            typeof(SectorMap)
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(map, new List<ClonedObjectBase>());
        }

        return map;
    }
}
