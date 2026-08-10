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

    [TestMethod]
    public void CanAcceptAnyOfCbid_FullCargoNoStack_ReturnsFalse()
    {
        // SS-31: cargo is completely full (no free 1x1 cell) and the cbid is non-stackable, so
        // neither the merge path nor the first-fit path can accept a single unit. Callers must
        // see false here BEFORE allocating firstCoid, or a placeholder row leaks for nothing.
        var clones = new FakeCloneBaseLookup();
        clones.Register(30, NonStackableClone(30));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 13);
        FillAllSlots(inventory);

        Assert.IsFalse(inventory.CanAcceptAnyOfCbid(30), "full cargo with a non-stackable cbid cannot accept any unit");
    }

    [TestMethod]
    public void CanAcceptAnyOfCbid_FullCargoWithMergeSpace_ReturnsTrue()
    {
        // SS-31: cargo grid has no free cell, but an existing stack of the SAME cbid has room
        // under maxStack. AddItemInternal's merge pass accepts this without ever touching the
        // grid, so the helper must agree — otherwise a legitimate merge-only claim would be
        // rejected before AddItemInternal even gets a chance to merge it.
        var clones = new FakeCloneBaseLookup();
        clones.Register(31, StackableClone(31, stackSize: 10));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 13);
        // Fill 12 of 13 slots with unrelated filler, and the last slot with a partial stack of
        // cbid 31 (quantity below maxStack) — the grid is completely full either way.
        var coid = 1000L;
        for (var row = 0; row < 12; row++)
        {
            inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "Filler", coid, 0, (byte)row, 1));
            coid++;
        }
        inventory.TryAdd(new CharacterInventoryItem(31, CloneBaseObjectType.Item, "Parts", coid, 0, 12, 1));

        Assert.IsTrue(inventory.CanAcceptAnyOfCbid(31), "a same-cbid stack with room under maxStack accepts a unit via merge even with a full grid");
    }

    [TestMethod]
    public void CanAcceptAnyOfCbid_UnresolvableFootprint_ReturnsFalse()
    {
        // SS-31: clonebase exists but InvSizeX/Y is zero — AddItemInternal rejects the whole add
        // in this case (footprint check runs before the merge pass), so the helper must return
        // false too even though there is plenty of free cargo space.
        var clones = new FakeCloneBaseLookup();
        clones.Register(32, ZeroFootprintClone(32));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 13); // entirely empty — would otherwise accept easily

        Assert.IsFalse(inventory.CanAcceptAnyOfCbid(32), "an unresolvable (zero) footprint is rejected outright, regardless of free space");
    }

    private static void FillAllSlots(InventoryManager inventory)
    {
        var coid = 1000L;
        for (var row = 0; row < 13; row++)
        {
            inventory.TryAdd(new CharacterInventoryItem(99, CloneBaseObjectType.Item, "Filler", coid, 0, (byte)row, 1));
            coid++;
        }
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

    private static CloneBaseObject StackableClone(int cbid, int stackSize)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            StackSize = (ushort)stackSize,
            InvSizeX = 1,
            InvSizeY = 1
        };
        return clone;
    }

    private static CloneBaseObject ZeroFootprintClone(int cbid)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            StackSize = 1,
            InvSizeX = 0,
            InvSizeY = 0
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
