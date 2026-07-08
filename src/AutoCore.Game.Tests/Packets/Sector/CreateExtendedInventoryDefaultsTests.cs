using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Packets.Sector;

[TestClass]
public class CreateExtendedInventoryDefaultsTests
{
    [TestMethod]
    public void CreateCharacterExtended_DefaultInventoryCoidsAreMinusOne()
    {
        var packet = new CreateCharacterExtendedPacket();

        Assert.AreEqual(312, packet.InventoryCoids.Length);
        Assert.IsTrue(packet.InventoryCoids.All(coid => coid == -1));
    }

    [TestMethod]
    public void CreateVehicleExtended_DefaultInventoryCoidsAreMinusOne()
    {
        var packet = new CreateVehicleExtendedPacket();

        Assert.AreEqual(512, packet.InventoryCoids.Length);
        Assert.IsTrue(packet.InventoryCoids.All(coid => coid == -1));
    }
}
