using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryCoidCounterTests
{
    [TestMethod]
    public void SyncFromCargo_NoOpWhenMapIsNull()
    {
        var harness = new InventoryTestHarness();
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 5000, 0, 0, 1));

        InventoryCoidCounter.SyncFromCargo(harness.Character);

        Assert.IsNull(harness.Character.Map);
    }

    [TestMethod]
    public void SyncFromCargo_NoOpWhenCargoIsEmpty()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 100);

        InventoryCoidCounter.SyncFromCargo(harness.Character);

        Assert.AreEqual(100, harness.Character.Map.LocalCoidCounter);
    }

    [TestMethod]
    public void SyncFromCargo_BumpsCounterWhenMaxCargoCoidIsGreaterOrEqual()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 1000);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1000, 0, 0, 1));
        harness.Inventory.TryAdd(new CharacterInventoryItem(11, CloneBaseObjectType.Item, "B", 1500, 1, 0, 1));

        InventoryCoidCounter.SyncFromCargo(harness.Character);

        Assert.AreEqual(1501, harness.Character.Map.LocalCoidCounter);
    }

    [TestMethod]
    public void SyncFromCargo_LeavesCounterWhenCargoMaxIsBelowCounter()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 2000);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 500, 0, 0, 1));

        InventoryCoidCounter.SyncFromCargo(harness.Character);

        Assert.AreEqual(2000, harness.Character.Map.LocalCoidCounter);
    }
}
