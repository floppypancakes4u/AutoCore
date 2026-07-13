using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerCapacityTests
{
    [TestMethod]
    public void DefaultCapacity_MatchesRetailStarterOnePage()
    {
        var inventory = new InventoryManager();

        // Retail FUN_004F3A30: width 6, one page of 13 rows (Callisto X InventorySlots=1).
        Assert.AreEqual(InventoryManager.DefaultCargoWidth, inventory.Width);
        Assert.AreEqual(InventoryManager.DefaultCargoPageCount, inventory.PageCount);
        Assert.AreEqual(InventoryManager.DefaultCargoSlotCount, inventory.SlotCount);
        Assert.AreEqual(6, inventory.Width);
        Assert.AreEqual(13, inventory.PageCount);
        Assert.AreEqual(78, inventory.SlotCount);
    }

    [TestMethod]
    public void SetCapacity_UpdatesSlotCountAndRejectsOutOfRangeSlots()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(8, 2);

        Assert.AreEqual(8, inventory.Width);
        Assert.AreEqual(2, inventory.PageCount);
        Assert.AreEqual(16, inventory.SlotCount);

        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(2, CloneBaseObjectType.Item, "B", 2, 8, 0, 1)));
        Assert.IsFalse(inventory.TryAdd(new CharacterInventoryItem(3, CloneBaseObjectType.Item, "C", 3, 0, 2, 1)));
        Assert.IsFalse(inventory.TryMove(1, 0, 2, out _));
    }

    [TestMethod]
    public void SetCapacity_ClampsToMaxPacketSlots()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(6, 200);

        Assert.AreEqual(6, inventory.Width);
        Assert.IsTrue(inventory.SlotCount <= InventoryManager.MaxCargoSlotCount);
        Assert.AreEqual(InventoryManager.MaxCargoSlotCount / 6, inventory.PageCount);
    }

    [TestMethod]
    public void TryGetFirstFreeCargoSlot_RespectsConfiguredCapacity()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(2, 1);

        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(1, CloneBaseObjectType.Item, "A", 1, 0, 0, 1)));
        Assert.IsTrue(inventory.TryAdd(new CharacterInventoryItem(2, CloneBaseObjectType.Item, "B", 2, 1, 0, 1)));
        Assert.IsTrue(inventory.IsFull);
        Assert.IsFalse(inventory.TryGetFirstFreeCargoSlot(out _, out _));
    }
}
