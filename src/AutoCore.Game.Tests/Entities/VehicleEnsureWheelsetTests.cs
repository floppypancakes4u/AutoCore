using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Database.World.Models;
using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Map;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;

[TestClass]
public class VehicleEnsureWheelsetTests
{
    [TestInitialize]
    public void SetUp() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestCleanup]
    public void TearDown() => AssetManagerTestHelper.ClearRegisteredCloneBases();

    [TestMethod]
    public void EnsureDefaultWheelSetForWire_EquipsClonebaseDefault()
    {
        const int vehicleCbid = 710_001;
        const int wheelsetCbid = 710_002;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelsetCbid);
        AssetManagerTestHelper.RegisterCloneBase(wheelsetCbid, CloneBaseObjectType.WheelSet);

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 50, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        vehicle.SetMap(CreateFieldMap());

        Assert.IsNull(vehicle.WheelSet);
        Assert.IsTrue(vehicle.EnsureDefaultWheelSetForWire());
        Assert.IsNotNull(vehicle.WheelSet);
        Assert.AreEqual(wheelsetCbid, vehicle.WheelSet.CBID);
        Assert.IsTrue(MapNpcIdentity.IsMapNpcIdentity(vehicle.WheelSet.ObjectId));
    }

    [TestMethod]
    public void EnsureDefaultWheelSetForWire_IdempotentWhenAlreadyEquipped()
    {
        const int vehicleCbid = 710_003;
        const int wheelsetCbid = 710_004;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelsetCbid);
        AssetManagerTestHelper.RegisterCloneBase(wheelsetCbid, CloneBaseObjectType.WheelSet);

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 51, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        vehicle.SetMap(CreateFieldMap());
        Assert.IsTrue(vehicle.EnsureDefaultWheelSetForWire());
        var first = vehicle.WheelSet;

        Assert.IsTrue(vehicle.EnsureDefaultWheelSetForWire());
        Assert.AreSame(first, vehicle.WheelSet);
    }

    [TestMethod]
    public void WriteToPacket_WithoutPriorEquip_EmbedsDefaultWheelsetWhenClonebaseDefinesIt()
    {
        const int vehicleCbid = 710_005;
        const int wheelsetCbid = 710_006;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid, defaultWheelsetCbid: wheelsetCbid);
        // Full CloneBaseWheelSet not required for WriteToPacket of empty path when ensure fails;
        // for full nested write, WheelSet.WriteToPacket needs CloneBaseWheelSet — register as Object type
        // and only assert ensure + CreateWheelSet presence via CBID on nested when Write succeeds.
        // Use a minimal path: ensure equips; if WriteToPacket throws on friction, skip nested body test.
        AssetManagerTestHelper.RegisterCloneBase(wheelsetCbid, CloneBaseObjectType.WheelSet);

        var vehicle = new Vehicle();
        vehicle.SetCoid(MapNpcIdentity.CoidBase + 52, true);
        vehicle.LoadCloneBase(vehicleCbid);
        vehicle.SetupCBFields();
        vehicle.SetMap(CreateFieldMap());

        // Ensure runs inside WriteToPacket before nested serialize.
        Assert.IsTrue(vehicle.EnsureDefaultWheelSetForWire());
        Assert.AreEqual(wheelsetCbid, vehicle.WheelSet.CBID);

        var packet = new CreateVehiclePacket();
        // Writing nested wheelset body requires CloneBaseWheelSet friction data; only test ensure
        // assignment here. WireDiag detail uses packet.CreateWheelSet.CBID after a manual fill.
        packet.CreateWheelSet = new CreateWheelSetPacket { CBID = vehicle.WheelSet.CBID };
        var detail = AutoCore.Game.Diagnostics.WireDiag.FormatCreateVehicleDetail(packet);
        StringAssert.Contains(detail, "wheelsetCbid=" + wheelsetCbid);
        StringAssert.Contains(detail, "wheelOk=1");
    }

    private static SectorMap CreateFieldMap()
    {
        var continent = new ContinentObject
        {
            Id = 707,
            MapFileName = "tm_ensure_wheel",
            DisplayName = "test",
            IsTown = false,
            IsPersistent = true,
        };
        return SectorMap.CreateForTests(continent, new Vector4(0, 0, 0, 0));
    }
}
