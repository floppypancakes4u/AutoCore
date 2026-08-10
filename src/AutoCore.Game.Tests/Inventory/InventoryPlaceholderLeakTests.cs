using System.Runtime.CompilerServices;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

/// <summary>
/// SS-31: AddItemInternal must confirm a free grid slot BEFORE allocating the next stack's
/// coid. Persistent item coids are reserved by inserting a placeholder simple_object row, so
/// allocating a coid for a stack that never gets placed leaks an orphan placeholder row.
/// </summary>
[TestClass]
public class InventoryPlaceholderLeakTests
{
    [TestMethod]
    public void AddItem_MultiStack_PartialFit_DoesNotAllocateCoidForUnplaceableStack()
    {
        // SS-31: cargo has exactly one free 1x1 slot; a non-stackable cbid requesting 2 units
        // needs 2 stacks. The first placement consumes the only free slot, so the second stack
        // can never be placed. allocateAdditionalCoid must not be invoked for that unplaceable
        // second stack — invoking it leaks an orphan simple_object placeholder row.
        var clones = new FakeCloneBaseLookup();
        clones.Register(20, NonStackableClone(20));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 13); // 1 column x 13 rows = 13 total 1x1 slots
        FillAllButOneSlot(inventory, freeRow: 12);

        var allocations = 0;
        long AllocateAdditionalCoid()
        {
            allocations++;
            return 9000 + allocations;
        }

        var result = inventory.AddItem(Entry(20), new TestItemCreator(), coid: 2000, quantity: 2,
            allocateAdditionalCoid: AllocateAdditionalCoid);

        Assert.AreEqual(0, allocations, "no coid should be allocated for a stack that never gets a grid slot");
        Assert.AreEqual(1, result.AcceptedQuantity, "only the one free slot's worth of quantity is accepted");
        Assert.AreEqual(1, result.RemainingQuantity, "the second, unplaceable unit remains unaccepted");
        Assert.IsNull(inventory.FindByCoid(9001), "no placeholder object exists for the coid that was never allocated");
    }

    [TestMethod]
    public void AddItem_MultiStack_AllFit_AllocatesExactlyOnePerAdditionalStack()
    {
        // SS-31 pin: happy path is unchanged by the reorder — k placed stacks still allocate
        // exactly k-1 additional coids (the first stack uses the caller-supplied firstCoid).
        var clones = new FakeCloneBaseLookup();
        clones.Register(21, NonStackableClone(21));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 13); // 13 free 1x1 slots, plenty of room for 3 stacks

        var allocations = 0;
        long AllocateAdditionalCoid()
        {
            allocations++;
            return 9000 + allocations;
        }

        var result = inventory.AddItem(Entry(21), new TestItemCreator(), coid: 3000, quantity: 3,
            allocateAdditionalCoid: AllocateAdditionalCoid);

        Assert.AreEqual(3, result.AcceptedQuantity);
        Assert.AreEqual(0, result.RemainingQuantity);
        Assert.AreEqual(2, allocations, "3 placed stacks allocate exactly 2 additional coids (k-1)");
        Assert.AreEqual(3, inventory.Items.Count);
        CollectionAssert.AreEqual(new long[] { 3000, 9001, 9002 },
            inventory.Items.OrderBy(i => i.Coid).Select(i => i.Coid).ToArray());
    }

    private static void FillAllButOneSlot(InventoryManager inventory, int freeRow)
    {
        var coid = 1000L;
        for (var row = 0; row < 13; row++)
        {
            if (row == freeRow)
                continue;

            inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "Filler", coid, 0, (byte)row, 1));
            coid++;
        }
    }

    private static InventoryCatalogEntry Entry(int cbid) => new(cbid, CloneBaseObjectType.Item, "Parts");

    private static CloneBaseObject NonStackableClone(int cbid)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            StackSize = 1,
            InvSizeX = 1,
            InvSizeY = 1
        };
        return clone;
    }

    private sealed class TestItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y) =>
            InventoryItemCreateResult.Success(new CreateSimpleObjectPacket
            {
                CBID = entry.Cbid,
                ObjectId = new(coid, true),
                InventoryPositionX = x,
                InventoryPositionY = y,
                IsInInventory = true
            }, entry.DisplayName);
    }
}
