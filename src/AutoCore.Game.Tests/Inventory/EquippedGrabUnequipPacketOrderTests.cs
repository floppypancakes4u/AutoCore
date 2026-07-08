using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class EquippedGrabUnequipPacketOrderTests
{
    [TestMethod]
    public void BuildEquippedGrabPackets_SendsUnequipBeforeGrabResponse()
    {
        var itemId = new TFID(9001, true);
        var vehicleId = new TFID(8001, true);

        var packets = InventoryManager.BuildEquippedGrabPackets(
            itemId,
            vehicleId,
            itemGlobal: true,
            inventoryType: 2);

        Assert.AreEqual(2, packets.Count);
        Assert.IsInstanceOfType(packets[0], typeof(InventoryUnequipPacket));
        Assert.IsInstanceOfType(packets[1], typeof(InventoryGrabResponsePacket));

        var unequip = (InventoryUnequipPacket)packets[0];
        Assert.AreEqual(9001, unequip.ItemId.Coid);
        Assert.AreEqual(8001, unequip.VehicleId.Coid);
        Assert.AreEqual(2, unequip.InventoryType);

        var grab = (InventoryGrabResponsePacket)packets[1];
        Assert.AreEqual(9001, grab.ItemCoid);
        Assert.IsTrue(grab.WasSuccessful);
        Assert.AreEqual(2, grab.InventoryType);
        Assert.IsFalse(grab.AddToExistingItem);
    }
}
