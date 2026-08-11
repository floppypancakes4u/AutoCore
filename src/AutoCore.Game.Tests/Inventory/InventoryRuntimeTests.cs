using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.EntityFrameworkCore;
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
    public void AllocateItemCoid_UsesPersistentAllocator_NotMapCounter()
    {
        // SS-31: item coids funded by Map.LocalCoidCounter collided with character/vehicle
        // coids from the simple_object sequence (item 18274/18275 vs character 18274 /
        // vehicle 18275 → EnsureSimpleObject clobbered them → client AV at character select).
        // Persistent item coids must come from the shared DB sequence, never the map counter.
        var harness = new InventoryTestHarness();
        InventoryTestMapHelper.AttachMap(harness.Character, localCoidCounter: 1000);
        harness.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "A", 1000, 0, 0, 1));

        var saved = InventoryRuntime.AllocatePersistentCoid;
        try
        {
            var next = 999_000L;
            InventoryRuntime.AllocatePersistentCoid = () => next++;

            var runtime = new InventoryRuntime(harness.Character);

            Assert.AreEqual(999_000L, runtime.AllocateItemCoid());
            Assert.AreEqual(999_001L, runtime.AllocateItemCoid());
            Assert.AreEqual(1000, harness.Character.Map.LocalCoidCounter,
                "map-local world counter must not fund persistent item coids");
        }
        finally
        {
            InventoryRuntime.AllocatePersistentCoid = saved;
        }
    }

    [TestMethod]
    public void AllocateFromSimpleObjectSequence_InsertsRowAndReturnsGeneratedCoid()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AutoCore.Database.Char.CharContext>()
            .UseInMemoryDatabase("item-coid-alloc-" + Guid.NewGuid().ToString("N"))
            .Options;
        var savedFactory = InventoryRuntime.CreateContext;
        try
        {
            InventoryRuntime.CreateContext = () => new AutoCore.Database.Char.CharContext(options);

            var first = InventoryRuntime.AllocateFromSimpleObjectSequence();
            var second = InventoryRuntime.AllocateFromSimpleObjectSequence();

            Assert.AreNotEqual(first, second, "sequence must be unique per allocation");
            using var verify = new AutoCore.Database.Char.CharContext(options);
            Assert.IsNotNull(verify.SimpleObjects.Find(first),
                "allocation must reserve the coid in simple_object so no other allocator can take it");
        }
        finally
        {
            InventoryRuntime.CreateContext = savedFactory;
        }
    }
}
