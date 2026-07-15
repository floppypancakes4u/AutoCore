using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryManagerFootprintTests
{
    [TestMethod]
    public void TryAdd_TwoByTwo_OccupiesFootprintCells()
    {
        var (inventory, _) = CreateWithSizes((10, 2, 2));

        Assert.IsTrue(inventory.TryAdd(Item(10, coid: 1, x: 0, y: 0)));

        Assert.AreEqual(4, inventory.GetOccupiedSlotCount());
        // Next free 1×1 must skip footprint
        Assert.IsTrue(inventory.TryFindFirstFreeCargoSlot(1, 1, out var x, out var y));
        Assert.AreEqual((byte)2, x);
        Assert.AreEqual((byte)0, y);
    }

    [TestMethod]
    public void TryAdd_RejectsItemOverlappingTwoByTwoFootprint()
    {
        var (inventory, _) = CreateWithSizes((10, 2, 2), (11, 1, 1));

        Assert.IsTrue(inventory.TryAdd(Item(10, coid: 1, x: 0, y: 0)));
        Assert.IsFalse(inventory.TryAdd(Item(11, coid: 2, x: 1, y: 0)));
        Assert.IsFalse(inventory.TryAdd(Item(11, coid: 2, x: 0, y: 1)));
        Assert.IsTrue(inventory.TryAdd(Item(11, coid: 2, x: 2, y: 0)));
    }

    [TestMethod]
    public void TryMove_TwoByTwo_RejectsPartialOverlap()
    {
        var (inventory, _) = CreateWithSizes((10, 2, 2), (11, 1, 1));

        Assert.IsTrue(inventory.TryAdd(Item(10, coid: 1, x: 0, y: 0)));
        Assert.IsTrue(inventory.TryAdd(Item(11, coid: 2, x: 3, y: 0)));

        Assert.IsFalse(inventory.TryMove(1, 2, 0, out _)); // would cover (2,0)(3,0)(2,1)(3,1) — hits item 2
        Assert.IsTrue(inventory.TryMove(1, 0, 2, out var moved));
        Assert.AreEqual((byte)0, moved.InventoryPositionX);
        Assert.AreEqual((byte)2, moved.InventoryPositionY);
    }

    [TestMethod]
    public void TryFindFirstFree_TwoByTwoPacksLikeClient()
    {
        var (inventory, _) = CreateWithSizes((10, 2, 2));

        Assert.IsTrue(inventory.TryFindFirstFreeCargoSlot(2, 2, out var x0, out var y0));
        Assert.AreEqual((0, 0), (x0, y0));
        Assert.IsTrue(inventory.TryAdd(Item(10, coid: 1, x: x0, y: y0)));

        Assert.IsTrue(inventory.TryFindFirstFreeCargoSlot(2, 2, out var x1, out var y1));
        Assert.AreEqual((2, 0), (x1, y1));
        Assert.IsTrue(inventory.TryAdd(Item(10, coid: 2, x: x1, y: y1)));

        Assert.IsTrue(inventory.TryFindFirstFreeCargoSlot(2, 2, out var x2, out var y2));
        Assert.AreEqual((4, 0), (x2, y2));
    }

    [TestMethod]
    public void TryFindFirstFree_ByCbid_UsesCloneBaseSize()
    {
        var (inventory, _) = CreateWithSizes((50, 3, 2));

        Assert.IsTrue(inventory.TryFindFirstFreeCargoSlotForCbid(50, out var x, out var y));
        Assert.AreEqual((byte)0, x);
        Assert.AreEqual((byte)0, y);

        // Place a blocker at (0,0) as 1×1 — 3×2 needs clear strip
        var (inventory2, lookup) = CreateWithSizes((50, 3, 2), (1, 1, 1));
        Assert.IsTrue(inventory2.TryAdd(Item(1, coid: 9, x: 0, y: 0)));
        Assert.IsTrue(inventory2.TryFindFirstFreeCargoSlotForCbid(50, out x, out y));
        Assert.AreEqual((byte)1, x);
        Assert.AreEqual((byte)0, y);
    }

    [TestMethod]
    public void TryFindFirstFree_ByCbid_UnknownSize_Fails()
    {
        var inventory = new InventoryManager(cloneBases: new FakeCloneBaseLookup());

        Assert.IsFalse(inventory.TryFindFirstFreeCargoSlotForCbid(999, out _, out _));
    }

    [TestMethod]
    public void AddItem_RejectsExplicitZeroFootprint()
    {
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(77, CreateClone(77, 0, 0));
        var inventory = new InventoryManager(cloneBases: lookup);
        var entry = new InventoryCatalogEntry(77, CloneBaseObjectType.Item, "Zero");

        var result = inventory.AddItem(entry, new ZeroSizeCreator(), coid: 1);

        Assert.IsNull(result.AddedItem);
        StringAssert.Contains(result.Message, "footprint");
    }

    private sealed class ZeroSizeCreator : IInventoryItemCreator
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

    private static (InventoryManager Inventory, FakeCloneBaseLookup Lookup) CreateWithSizes(
        params (int Cbid, byte SizeX, byte SizeY)[] sizes)
    {
        var lookup = new FakeCloneBaseLookup();
        foreach (var (cbid, sx, sy) in sizes)
            lookup.Register(cbid, CreateClone(cbid, sx, sy));

        return (new InventoryManager(cloneBases: lookup), lookup);
    }

    private static CharacterInventoryItem Item(int cbid, long coid, byte x, byte y) =>
        new(cbid, CloneBaseObjectType.Item, $"item{cbid}", coid, x, y, 1);

    private static CloneBaseObject CreateClone(int cbid, byte sizeX, byte sizeY)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { InvSizeX = sizeX, InvSizeY = sizeY };
        return clone;
    }
}
