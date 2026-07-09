using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class HardpointEquipPacketOrderTests
{
    [TestMethod]
    public void BuildHardpointEquipPackets_SendsEquipOnly_NotDropResponse()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(8001, true);

        var packets = InventoryManager.BuildHardpointEquipPackets(
            inventory: null,
            vehicle,
            newItemId: new TFID(9001, true),
            oldItemId: null,
            sourceInventoryType: 1);

        Assert.AreEqual(1, packets.Count);
        Assert.IsInstanceOfType(packets[0], typeof(InventoryEquipPacket));
        Assert.IsFalse(packets.Any(p => p is InventoryDropResponsePacket));

        var equip = (InventoryEquipPacket)packets[0];
        Assert.AreEqual(9001, equip.ItemId.Coid);
        Assert.AreEqual(8001, equip.VehicleId.Coid);
        Assert.AreEqual(-1, equip.OldItemId.Coid);
        Assert.AreEqual(1, equip.InventoryTypeFrom);
        Assert.IsTrue(equip.PutInHand);
        Assert.AreEqual(0, equip.InventoryPositionX);
        Assert.AreEqual(0, equip.InventoryPositionY);
    }

    [TestMethod]
    public void BuildHardpointEquipPackets_IncludesOldItemWhenSwapping()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(8001, true);

        var packets = InventoryManager.BuildHardpointEquipPackets(
            inventory: null,
            vehicle,
            newItemId: new TFID(9002, true),
            oldItemId: new TFID(9001, true),
            sourceInventoryType: 1);

        var equip = (InventoryEquipPacket)packets[0];
        Assert.AreEqual(9001, equip.OldItemId.Coid);
        Assert.IsTrue(equip.PutInHand);
    }

    [TestMethod]
    public void BuildHardpointEquipPackets_IncludesCargoSendAllWhenInventoryProvided()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(8001, true);
        var inventory = new InventoryManager();

        var packets = InventoryManager.BuildHardpointEquipPackets(
            inventory,
            vehicle,
            newItemId: new TFID(9001, true),
            oldItemId: null,
            sourceInventoryType: 1);

        Assert.AreEqual(2, packets.Count);
        Assert.IsInstanceOfType(packets[0], typeof(InventoryEquipPacket));
        Assert.IsInstanceOfType(packets[1], typeof(InventoryCargoSendAllPacket));
    }
}
