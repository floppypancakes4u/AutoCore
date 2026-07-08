using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerEdgeTests
{
    [TestMethod]
    public void CreateGrabFailure_MapsPacketFields()
    {
        var failure = InventoryManager.CreateGrabFailure(
            InventoryTestHarness.CreateGrabPacket(12345, inventoryType: 2, itemGlobal: true));

        Assert.AreEqual(12345, failure.ItemCoid);
        Assert.IsTrue(failure.ItemGlobal);
        Assert.AreEqual(2, failure.InventoryType);
        Assert.IsFalse(failure.WasSuccessful);
        Assert.AreEqual(1, failure.Quantity);
    }

    [TestMethod]
    public void CreateDropFailure_MapsPacketFields()
    {
        var failure = InventoryManager.CreateDropFailure(
            InventoryTestHarness.CreateDropPacket(54321, x: 4, y: 2, inventoryType: 1, itemGlobal: false));

        Assert.AreEqual(54321, failure.ItemCoid);
        Assert.IsFalse(failure.ItemGlobal);
        Assert.AreEqual((byte)4, failure.InventoryPositionX);
        Assert.AreEqual((byte)2, failure.InventoryPositionY);
        Assert.AreEqual((byte)1, failure.InventoryType);
        Assert.IsFalse(failure.WasSuccessful);
        Assert.IsFalse(failure.HasSwappedOrConcatenatedItem);
    }

    [TestMethod]
    public void LoadItems_FiltersInvalidSlotsAndNullEntries()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(4, 1);

        inventory.LoadItems(new CharacterInventoryItem[]
        {
            null,
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "Valid", 100, 0, 0, 1),
            new CharacterInventoryItem(2, CloneBaseObjectType.Item, "InvalidX", 101, 9, 0, 1),
            new CharacterInventoryItem(3, CloneBaseObjectType.Item, "InvalidY", 102, 0, 2, 1),
        });

        Assert.AreEqual(1, inventory.Items.Count);
        Assert.AreEqual(100, inventory.FindByCoid(100).Coid);
    }

    [TestMethod]
    public void LoadItems_NullCollectionClearsItems()
    {
        var inventory = new InventoryManager();
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        inventory.LoadItems(null);
        Assert.AreEqual(0, inventory.Items.Count);
    }

    [TestMethod]
    public void CreateCargoSendAll_WithCustomCapacity_MapsItems()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(4, 2);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 100, 1, 1, 1));

        var packet = InventoryPacketFactory.CreateCargoSendAll(inventory);

        Assert.AreEqual(2, packet.InventorySize);
        Assert.AreEqual(100, packet.Items[5].ItemCoid);
        Assert.AreEqual((byte)1, packet.Items[5].PositionX);
        Assert.AreEqual((byte)1, packet.Items[5].PositionY);
    }

    [TestMethod]
    public void CreateCargoSendAll_NullInventory_UsesDefaults()
    {
        var packet = InventoryPacketFactory.CreateCargoSendAll(null);
        Assert.AreEqual(InventoryManager.DefaultCargoPageCount, packet.InventorySize);
    }

    [TestMethod]
    public void ConfigureVehicleCargo_WithInventory_FillsExtendedCoids()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(4, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 100, 2, 0, 1));

        var packet = new CreateVehicleExtendedPacket();
        InventoryPacketFactory.ConfigureVehicleCargo(packet, inventory);

        Assert.AreEqual(inventory.SlotCount, packet.NumInventorySlots);
        Assert.AreEqual(100, packet.InventoryCoids[2]);
    }

    [TestMethod]
    public void ConfigureVehicleCargo_NullInventory_UsesDefaultSlotCount()
    {
        var packet = new CreateVehicleExtendedPacket();
        InventoryPacketFactory.ConfigureVehicleCargo(packet, null);

        Assert.AreEqual(InventoryManager.DefaultCargoSlotCount, packet.InventorySlots);
        Assert.AreEqual(InventoryManager.DefaultCargoSlotCount, packet.NumInventorySlots);
    }

    [TestMethod]
    public void AddItem_WithPersistence_RecordsUpsertAndReturnsPackets()
    {
        var persistence = new Fakes.RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var entry = new InventoryCatalogEntry(10, CloneBaseObjectType.Item, "Widget");
        var creator = new FakeItemCreator();

        var result = inventory.AddItem(entry, creator, coid: 1001, characterCoid: 5001);

        Assert.IsNotNull(result.AddedItem);
        Assert.AreEqual(1, persistence.Upserted.Count);
        Assert.AreEqual(3, result.Packets.Count);
    }

    [TestMethod]
    public void AddItem_WhenCargoFull_ReturnsFailure()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1));
        var entry = new InventoryCatalogEntry(2, CloneBaseObjectType.Item, "B");

        var result = inventory.AddItem(entry, new FakeItemCreator(), coid: 2);

        Assert.IsNull(result.AddedItem);
        StringAssert.Contains(result.Message, "full");
    }

    [TestMethod]
    public void SaveCapacity_RecordsThroughPersistence()
    {
        var persistence = new Fakes.RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        inventory.SetCapacity(8, 3);
        inventory.SaveCapacity(5001);

        Assert.AreEqual(1, persistence.CapacitySaves.Count);
        Assert.AreEqual((5001, 8, 3), persistence.CapacitySaves[0]);
    }

    private sealed class FakeItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new(coid, true),
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true
                },
                entry.DisplayName);
    }
}
