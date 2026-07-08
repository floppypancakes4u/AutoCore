using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerTests
{
    [TestMethod]
    public void TryGetFirstFreeCargoSlot_SkipsOccupiedSlots()
    {
        var inventory = new InventoryManager();
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "First", 1001, 0, 0, 1)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(21, CloneBaseObjectType.Item, "Second", 1002, 1, 0, 1)));

        Assert.IsTrue(inventory.TryGetFirstFreeCargoSlot(out var x, out var y));

        Assert.AreEqual((byte)2, x);
        Assert.AreEqual((byte)0, y);
    }

    [TestMethod]
    public void TryAdd_RejectsOccupiedSlot()
    {
        var inventory = new InventoryManager();

        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "First", 1001, 0, 0, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(21, CloneBaseObjectType.Item, "Second", 1002, 0, 0, 1)));
    }

    [TestMethod]
    public void TryMove_RejectsMissingItemInvalidSlotAndOccupiedSlot()
    {
        var inventory = new InventoryManager();
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "First", 1001, 0, 0, 1)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(21, CloneBaseObjectType.Item, "Second", 1002, 1, 0, 1)));

        Assert.IsFalse(inventory.TryMove(9999, 2, 0, out _));
        Assert.IsFalse(inventory.TryMove(1001, InventoryManager.CargoWidth, 0, out _));
        Assert.IsFalse(inventory.TryMove(1001, 1, 0, out _));
    }

    [TestMethod]
    public void CreateItemObjectPackets_RecreatesInventoryObjectPacketsInSlotOrder()
    {
        var inventory = new InventoryManager();
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(20, CloneBaseObjectType.Item, "Second", 1002, 1, 0, 3)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "First", 1001, 0, 0, 2)));

        var packets = inventory.CreateItemObjectPackets(
            new InventoryCatalog(() => new[]
            {
                new InventoryCatalogEntry(10, CloneBaseObjectType.Item, "First"),
                new InventoryCatalogEntry(20, CloneBaseObjectType.Item, "Second")
            }),
            new FakeInventoryItemCreator());

        Assert.AreEqual(2, packets.Count);

        var first = (CreateSimpleObjectPacket)packets[0];
        Assert.AreEqual(10, first.CBID);
        Assert.AreEqual(1001, first.ObjectId.Coid);
        Assert.AreEqual((byte)0, first.InventoryPositionX);
        Assert.AreEqual((byte)0, first.InventoryPositionY);
        Assert.AreEqual(2, first.Quantity);
        Assert.IsTrue(first.IsInInventory);

        var second = (CreateSimpleObjectPacket)packets[1];
        Assert.AreEqual(20, second.CBID);
        Assert.AreEqual(1002, second.ObjectId.Coid);
        Assert.AreEqual((byte)1, second.InventoryPositionX);
        Assert.AreEqual((byte)0, second.InventoryPositionY);
        Assert.AreEqual(3, second.Quantity);
        Assert.IsTrue(second.IsInInventory);
    }

    private sealed class FakeInventoryItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            return InventoryItemCreateResult.Success(
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
}
