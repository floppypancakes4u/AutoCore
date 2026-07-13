using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// World loot pickup must use the same cargo + client wire sequence as /addItem:
/// Create (IsInInventory) → InventoryAddItemResponse (0x2047) → CargoSendAll.
/// Reusing the world local TFID with only 0x2047 does not place the item in cargo on the client.
/// </summary>
[TestClass]
public class WorldItemPickupTests
{
    [TestMethod]
    public void PickupWorldItem_MatchesAddItemPacketOrderAndCreatesInventoryObject()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Existing", 1000, 0, 0, 1));

        // World loot had local coid 555; inventory claim must use a fresh inventory coid (like /addItem).
        const long worldLocalCoid = 555;
        const long inventoryCoid = 2001;
        const int lootCbid = 20;

        var result = inventory.PickupWorldItem(
            cbid: lootCbid,
            type: CloneBaseObjectType.Item,
            displayName: "Ground Loot",
            inventoryCoid: inventoryCoid,
            itemCreator: new FakePickupItemCreator(),
            characterCoid: 5001);

        Assert.IsNotNull(result.AddedItem, result.Message);
        Assert.AreEqual(3, result.Packets.Count, "Must send Create + AddItemResponse + CargoSendAll like /addItem.");
        Assert.IsInstanceOfType(result.Packets[0], typeof(CreateSimpleObjectPacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryAddItemResponsePacket));
        Assert.IsInstanceOfType(result.Packets[2], typeof(InventoryCargoSendAllPacket));

        var create = (CreateSimpleObjectPacket)result.Packets[0];
        Assert.IsTrue(create.IsInInventory, "Create packet must mark inventory so client binds cargo entity.");
        Assert.AreEqual(lootCbid, create.CBID);
        Assert.AreEqual(inventoryCoid, create.ObjectId.Coid);
        Assert.IsTrue(create.ObjectId.Global, "Inventory items use global TFID like /addItem.");
        Assert.AreEqual((byte)1, create.InventoryPositionX);
        Assert.AreEqual((byte)0, create.InventoryPositionY);
        Assert.AreNotEqual(worldLocalCoid, create.ObjectId.Coid, "Must not reuse world local coid for cargo create.");

        var response = (InventoryAddItemResponsePacket)result.Packets[1];
        Assert.IsTrue(response.WasSuccessful);
        Assert.AreEqual(inventoryCoid, response.ItemCoid);
        Assert.AreEqual((byte)1, response.InventoryPositionX);
        Assert.AreEqual((byte)0, response.InventoryPositionY);
        Assert.AreEqual(1, response.Quantity);
        Assert.IsFalse(response.AddToExistingItem);

        var cargo = (InventoryCargoSendAllPacket)result.Packets[2];
        Assert.IsTrue(cargo.Items.Any(i => i.ItemCoid == inventoryCoid), "CargoSendAll must include the picked item.");
        Assert.IsTrue(cargo.Items.Any(i => i.ItemCoid == 1000), "CargoSendAll must keep existing cargo.");

        var stored = inventory.FindByCoid(inventoryCoid);
        Assert.IsNotNull(stored);
        Assert.AreEqual(lootCbid, stored.Cbid);
        Assert.AreEqual(1, stored.InventoryPositionX);
        Assert.AreEqual(0, stored.InventoryPositionY);

        Assert.AreEqual(1, persistence.Upserted.Count);
        Assert.AreEqual(5001, persistence.Upserted[0].CharacterCoid);
        Assert.AreEqual(inventoryCoid, persistence.Upserted[0].Item.Coid);
    }

    [TestMethod]
    public void PickupWorldItem_WhenCargoFull_DoesNotAddOrPersist()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Full", 1, 0, 0, 1));

        var result = inventory.PickupWorldItem(
            cbid: 20,
            type: CloneBaseObjectType.Item,
            displayName: "Loot",
            inventoryCoid: 99,
            itemCreator: new FakePickupItemCreator(),
            characterCoid: 5001);

        Assert.IsNull(result.AddedItem);
        Assert.AreEqual(0, result.Packets.Count);
        Assert.AreEqual(0, persistence.Upserted.Count);
        Assert.IsNull(inventory.FindByCoid(99));
    }

    [TestMethod]
    public void PickupWorldItem_RejectsNonInventoryTypes()
    {
        var inventory = new InventoryManager();
        var result = inventory.PickupWorldItem(
            cbid: 30,
            type: CloneBaseObjectType.Creature,
            displayName: "Not Loot",
            inventoryCoid: 1,
            itemCreator: new FakePickupItemCreator(),
            characterCoid: 5001);

        Assert.IsNull(result.AddedItem);
        Assert.AreEqual(0, result.Packets.Count);
        StringAssert.Contains(result.Message, "not an inventory item");
    }

    private sealed class FakePickupItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new TFID(coid, global: true),
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true,
                    IsIdentified = true,
                    IsBound = false
                },
                entry.DisplayName);
    }
}
