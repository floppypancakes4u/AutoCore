using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryPacketFactoryTests
{
    [TestMethod]
    public void ConfigureVehicleCargo_AdvertisesPageCountOnWire()
    {
        var packet = new CreateVehicleExtendedPacket();

        InventoryPacketFactory.ConfigureVehicleCargo(packet);

        // Default manager is 1 retail page (6×13); wire short is UI page count.
        Assert.AreEqual(1, packet.InventorySlots);
        Assert.AreEqual(1, packet.NumInventorySlots);
        Assert.AreEqual(78, packet.InventorySize);
        Assert.IsTrue(packet.InventoryCoids.All(coid => coid == -1));
    }

    [TestMethod]
    public void ConfigureVehicleCargo_EmitsManagerItemCoids()
    {
        var inventory = new InventoryManager();
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "New Item", 1001, 1, 0, 1)));
        var packet = new CreateVehicleExtendedPacket();

        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        Assert.AreEqual(-1, packet.InventoryCoids[0]);
        Assert.AreEqual(1001, packet.InventoryCoids[1]);
    }

    [TestMethod]
    public void CreateCargoSendAll_UsesCargoPageCountAndPlacesItemsBySlot()
    {
        var inventory = new InventoryManager();
        inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "New Item", 1001, 1, 0, 1));

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);

        // InventorySize on CargoSendAll is UI page count (height/13), not grid height.
        Assert.AreEqual(1, packet.InventorySize);
        Assert.AreEqual(-1, packet.Items[0].ItemCoid);
        Assert.AreEqual(1001, packet.Items[1].ItemCoid);
        Assert.AreEqual(1, packet.Items[1].PositionX);
        Assert.AreEqual(0, packet.Items[1].PositionY);
    }

    [TestMethod]
    public void TryAdd_RejectsDuplicateCoid()
    {
        var inventory = new InventoryManager();

        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "First", 1001, 0, 0, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(21, CloneBaseObjectType.Item, "Duplicate", 1001, 1, 0, 1)));
    }

    [TestMethod]
    public void TryMove_UpdatesSlotAndRejectsOccupiedDestination()
    {
        var inventory = new InventoryManager();
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "First", 1001, 0, 0, 1)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(21, CloneBaseObjectType.Item, "Second", 1002, 1, 0, 1)));

        Assert.IsFalse(inventory.TryMove(1001, 1, 0, out _));

        Assert.IsTrue(inventory.TryMove(1001, 2, 0, out var moved));
        Assert.AreEqual(2, moved.InventoryPositionX);
        Assert.AreEqual(0, moved.InventoryPositionY);
        Assert.AreEqual(2, inventory.FindByCoid(1001).InventoryPositionX);
        Assert.AreEqual(0, inventory.FindByCoid(1001).InventoryPositionY);
    }
}
