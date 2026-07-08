using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
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
}
