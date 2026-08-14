using AutoCore.Game.Constants;
using AutoCore.Game.Map;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using AutoCore.Game.TNL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.TNL;

using AutoCore.Game.Entities;

/// <summary>
/// PDB Pass 6 sequence locks.
/// <c>CVOGCreature_PostCreateFromPacket</c> 0x004c5c30 looks up packet+0xF8 via
/// <c>FUN_004bafe0</c> and only then calls <c>FUN_004c49d0</c> (SetVehicle).
/// Missing vehicle → skip bind (not AV). Ghost-synthesized create leaves +0xF8 = −1
/// (<c>FUN_005d2520</c>), so CreateVehicle must precede CreateCreature(driver).
/// </summary>
[TestClass]
public class CreatureCreateOrderingTests
{
    [TestInitialize]
    public void SetUp()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestCleanup]
    public void TearDown()
    {
        TNLConnection.TestPacketSink = null;
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void DriverCreate_PlacesVehicleCoidAtClientPlusF8()
    {
        var (vehicle, driver) = ArrangeVehicleWithCreatureDriver();
        Assert.IsTrue(ForeignNpcDriverWire.TryBuildDriverCreate(vehicle, out var packet));
        Assert.AreEqual(vehicle.ObjectId.Coid, packet.CoidCurrentVehicle);
        Assert.AreEqual(driver.ObjectId.Coid, packet.ObjectId.Coid);
        Assert.AreEqual(GameOpcode.CreateCreature, packet.Opcode);
    }

    [TestMethod]
    public void DriverSend_IsCreateCreatureAfterCallerSendsCreateVehicle()
    {
        var (vehicle, _) = ArrangeVehicleWithCreatureDriver();
        var connection = new TNLConnection();
        var packets = new List<BasePacket>();
        TNLConnection.TestPacketSink = (_, p) => packets.Add(p);

        var createVehicle = new CreateVehiclePacket();
        vehicle.WriteToPacket(createVehicle);
        connection.SendGamePacket(createVehicle);
        Assert.IsTrue(ForeignNpcDriverWire.TrySendDriverCreate(connection, vehicle));

        Assert.AreEqual(2, packets.Count);
        Assert.IsInstanceOfType(packets[0], typeof(CreateVehiclePacket));
        Assert.IsInstanceOfType(packets[1], typeof(CreateCreaturePacket));
        Assert.AreEqual(vehicle.ObjectId.Coid, ((CreateCreaturePacket)packets[1]).CoidCurrentVehicle);
    }

    [TestMethod]
    public void ForeignVehicleHold_StillBlocksGhostUntilCreatePump()
    {
        var savedMs = TNLConnection.ForeignGhostScopeHoldMilliseconds;
        var savedQueries = TNLConnection.ForeignGhostScopeHoldQueries;
        try
        {
            TNLConnection.ForeignGhostScopeHoldMilliseconds = 500;
            TNLConnection.ForeignGhostScopeHoldQueries = 1;
            var conn = new TNLConnection();
            const long coid = MapNpcIdentity.CoidBase + 70_001;
            conn.NoteForeignVehicleCreateSent(coid);
            Assert.IsTrue(conn.HasActiveForeignCreateHold(coid));
            Assert.IsFalse(conn.TryAllowForeignVehicleGhostScope(coid),
                "Pass 5 hold must remain: FUN_008078B0 applies ghosts before game packets.");
        }
        finally
        {
            TNLConnection.ForeignGhostScopeHoldMilliseconds = savedMs;
            TNLConnection.ForeignGhostScopeHoldQueries = savedQueries;
        }
    }

    private static (Vehicle vehicle, Creature driver) ArrangeVehicleWithCreatureDriver()
    {
        const int vehicleCbid = 800_100;
        const int driverCbid = 800_101;
        AssetManagerTestHelper.RegisterCreatureCloneBase(driverCbid, maxHitPoint: 40);
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: 40);

        var driver = new Creature { Level = 4 };
        driver.SetCoid(MapNpcIdentity.CoidBase + 71, true);
        driver.LoadCloneBase(driverCbid);
        driver.SetupCBFields();

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 70, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        vehicle.SetOwner(driver);
        return (vehicle, driver);
    }
}
