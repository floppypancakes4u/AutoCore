using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Entities;

using AutoCore.Game.Entities;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;

/// <summary>
/// Stage 4: Vehicle.WriteToPacket must feed real NPC path/template fields into the
/// CreateVehiclePacket instead of the hardcoded placeholder values.
/// </summary>
[TestClass]
public class VehicleWireTests
{
    [TestInitialize]
    public void TestInitialize()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        AssetManagerTestHelper.ClearRegisteredCloneBases();
    }

    [TestMethod]
    public void WriteToPacket_PathFields_RoundTrip()
    {
        const int vehicleCbid = 640_001;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9301, true);
        vehicle.LoadCloneBase(vehicleCbid);

        vehicle.CoidCurrentPath = 777;
        vehicle.ExtraPathId = 3;
        vehicle.PatrolDistance = 42.5f;
        vehicle.PathReversing = true;
        vehicle.PathIsRoad = false;
        vehicle.TemplateId = 88;

        var packet = new CreateVehiclePacket();
        vehicle.WriteToPacket(packet);

        Assert.AreEqual(777, packet.CurrentPathId);
        Assert.AreEqual(3, packet.ExtraPathId);
        Assert.AreEqual(42.5f, packet.PatrolDistance);
        Assert.IsTrue(packet.PathReversing);
        Assert.IsFalse(packet.PathIsRoad);
        Assert.AreEqual(88, packet.TemplateId);
    }

    [TestMethod]
    public void WriteToPacket_NoPath_WritesDefaults()
    {
        const int vehicleCbid = 640_002;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9302, true);
        vehicle.LoadCloneBase(vehicleCbid);

        var packet = new CreateVehiclePacket();
        vehicle.WriteToPacket(packet);

        Assert.AreEqual(-1, packet.CurrentPathId);
        Assert.AreEqual(-1, packet.ExtraPathId);
        Assert.AreEqual(0f, packet.PatrolDistance);
        Assert.IsFalse(packet.PathReversing);
        Assert.IsFalse(packet.PathIsRoad);
        Assert.AreEqual(-1, packet.TemplateId);
    }
}
