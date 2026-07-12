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

    /// <summary>
    /// Ghidra RE (2026-07-11): the client links driverCreature+0x250 = vehicle (and the
    /// vehicle-side owner pointer) ONLY from CreateVehiclePacket.CoidCurrentOwner (+0xd8) in
    /// Vehicle_applyCreatePacket — the ghost CurrentOwner block is parsed but ignored on the
    /// bind-only path because AutoCore pre-sends CreateVehicle. Owner 0 → no link → target-frame
    /// HP text never renders for NPC vehicles.
    /// </summary>
    [TestMethod]
    public void WriteToPacket_NpcVehicleWithDriver_SendsDriverAsCurrentOwner()
    {
        const int vehicleCbid = 640_010;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9310, true);
        vehicle.LoadCloneBase(vehicleCbid);

        var driver = new Creature();
        driver.SetCoid(9311, true);
        vehicle.SetOwner(driver);

        var packet = new CreateVehiclePacket();
        vehicle.WriteToPacket(packet);

        Assert.AreEqual(9311L, packet.CoidCurrentOwner,
            "NPC CreateVehicle must carry the driver creature coid so the client attaches driver+0x250 = vehicle.");
    }

    [TestMethod]
    public void WriteToPacket_NoDriverNoDb_CurrentOwnerZero()
    {
        const int vehicleCbid = 640_011;
        AssetManagerTestHelper.RegisterVehicleCloneBase(vehicleCbid);

        var vehicle = new Vehicle();
        vehicle.SetCoid(9312, true);
        vehicle.LoadCloneBase(vehicleCbid);

        var packet = new CreateVehiclePacket();
        vehicle.WriteToPacket(packet);

        Assert.AreEqual(0L, packet.CoidCurrentOwner);
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
