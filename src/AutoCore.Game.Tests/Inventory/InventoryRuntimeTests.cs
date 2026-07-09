using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryRuntimeTests
{
    [TestMethod]
    public void InventoryRuntime_ExposesCharacterInventoryAndCoid()
    {
        var harness = new InventoryTestHarness(characterCoid: 4242);
        var runtime = new InventoryRuntime(harness.Character);

        Assert.AreSame(harness.Inventory, runtime.Inventory);
        Assert.AreEqual(4242, runtime.CharacterCoid);
        Assert.IsFalse(runtime.CanAllocateItem);
    }

    [TestMethod]
    public void InventoryRuntime_WithNullCharacter_ReturnsDefaults()
    {
        var runtime = new InventoryRuntime(null);
        Assert.IsNull(runtime.Inventory);
        Assert.AreEqual(0, runtime.CharacterCoid);
        Assert.IsFalse(runtime.CanAllocateItem);
    }

    [TestMethod]
    public void CanAllocateItem_IsTrueWhenCharacterHasMap()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character);
        var runtime = new InventoryRuntime(harness.Character);

        Assert.IsTrue(runtime.CanAllocateItem);
    }

    [TestMethod]
    public void AllocateItemCoid_SkipsOccupiedCargoCoids()
    {
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 1000);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1000, 0, 0, 1));
        harness.Inventory.TryAdd(new CharacterInventoryItem(11, CloneBaseObjectType.Item, "B", 1001, 1, 0, 1));

        var runtime = new InventoryRuntime(harness.Character);
        var allocated = runtime.AllocateItemCoid();

        Assert.AreEqual(1002, allocated);
        Assert.AreEqual(1003, harness.Character.Map.LocalCoidCounter);
    }
}
