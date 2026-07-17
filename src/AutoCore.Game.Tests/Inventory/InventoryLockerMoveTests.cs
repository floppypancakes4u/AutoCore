using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// Cargo ↔ locker moves use InventoryGrab (0x2034) + InventoryDrop (0x2036)
/// with ucTypeFrom/ucTypeTo = <see cref="InventoryTypes.Locker"/> (3).
/// </summary>
[TestClass]
public class InventoryLockerMoveTests
{
    [TestMethod]
    public void Grab_FromLocker_SucceedsWithType3()
    {
        var harness = new InventoryTestHarness();
        Assert.IsTrue(harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 1, 2, 1)));

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001, inventoryType: InventoryTypes.Locker),
            harness.Character);

        var response = (InventoryGrabResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(InventoryTypes.Locker, response.InventoryType);
        Assert.AreEqual((byte)1, response.InventoryPositionX);
        Assert.AreEqual((byte)2, response.InventoryPositionY);
        Assert.IsNotNull(harness.Inventory.FindLockerByCoid(1001), "Grab does not remove from locker (cursor model)");
    }

    [TestMethod]
    public void Drop_CargoToLocker_MovesItemAndReturnsDropResponseType3()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 2, y: 1, inventoryType: InventoryTypes.Locker),
            harness.Character);

        Assert.IsNull(harness.Inventory.FindByCoid(1001), "Item must leave cargo");
        var lockerItem = harness.Inventory.FindLockerByCoid(1001);
        Assert.IsNotNull(lockerItem);
        Assert.AreEqual((byte)2, lockerItem.InventoryPositionX);
        Assert.AreEqual((byte)1, lockerItem.InventoryPositionY);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(InventoryTypes.Locker, response.InventoryType);
        Assert.AreEqual((byte)2, response.InventoryPositionX);
        Assert.AreEqual((byte)1, response.InventoryPositionY);

        Assert.AreEqual(1, harness.Persistence.DeletedItemCoids.Count, "Cargo row deleted");
        Assert.AreEqual(1, harness.Persistence.LockerUpserted.Count, "Locker row upserted");
    }

    [TestMethod]
    public void Drop_LockerToCargo_MovesItemAndReturnsDropResponseType1()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 3, y: 2, inventoryType: InventoryTypes.Cargo),
            harness.Character);

        Assert.IsNull(harness.Inventory.FindLockerByCoid(1001));
        var cargoItem = harness.Inventory.FindByCoid(1001);
        Assert.IsNotNull(cargoItem);
        Assert.AreEqual((byte)3, cargoItem.InventoryPositionX);
        Assert.AreEqual((byte)2, cargoItem.InventoryPositionY);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(InventoryTypes.Cargo, response.InventoryType);
        Assert.AreEqual(1, harness.Persistence.LockerDeletedItemCoids.Count);
        Assert.AreEqual(1, harness.Persistence.Upserted.Count);
    }

    [TestMethod]
    public void Drop_LockerRearrange_UpdatesLockerSlot()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Widget", 1001, 0, 0, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 4, y: 5, inventoryType: InventoryTypes.Locker),
            harness.Character);

        var item = harness.Inventory.FindLockerByCoid(1001);
        Assert.IsNotNull(item);
        Assert.AreEqual((byte)4, item.InventoryPositionX);
        Assert.AreEqual((byte)5, item.InventoryPositionY);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(InventoryTypes.Locker, response.InventoryType);
        Assert.AreEqual(1, harness.Persistence.LockerMoved.Count);
    }

    [TestMethod]
    public void Drop_CargoToOccupiedLockerSlot_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Mover", 1001, 0, 0, 1));
        harness.Inventory.TryAddLocker(
            new CharacterInventoryItem(11, CloneBaseObjectType.Item, "Blocker", 2002, 2, 1, 1));

        var result = harness.Inventory.Drop(
            InventoryTestHarness.CreateDropPacket(1001, x: 2, y: 1, inventoryType: InventoryTypes.Locker),
            harness.Character);

        var response = (InventoryDropResponsePacket)result.Packets[0];
        Assert.IsFalse(response.WasSuccessful);
        Assert.IsNotNull(harness.Inventory.FindByCoid(1001), "Mover stays in cargo on failure");
        Assert.IsNull(harness.Inventory.FindLockerByCoid(1001));
    }

    [TestMethod]
    public void Grab_FromLocker_MissingItem_Fails()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "CargoOnly", 1001, 0, 0, 1));

        var result = harness.Inventory.Grab(
            InventoryTestHarness.CreateGrabPacket(1001, inventoryType: InventoryTypes.Locker),
            harness.Character);

        Assert.IsFalse(((InventoryGrabResponsePacket)result.Packets[0]).WasSuccessful);
    }

    [TestMethod]
    public void LoadLockerItems_RestoresIntoLockerContainer()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.LoadLockerItems(new[]
        {
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 50, 1, 0, 2)
        });

        var item = harness.Inventory.FindLockerByCoid(50);
        Assert.IsNotNull(item);
        Assert.AreEqual(2, item.Quantity);
        Assert.AreEqual(0, harness.Inventory.Items.Count);
    }
}
