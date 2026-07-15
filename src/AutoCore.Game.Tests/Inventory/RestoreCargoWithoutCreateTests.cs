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
public class RestoreCargoWithoutCreateTests
{
    [TestMethod]
    public void RestoreCargoWithoutCreate_PlacesOriginalCoid_NoCreatePacket()
    {
        var inv = new InventoryManager();
        var item = new CharacterInventoryItem(
            19194, CloneBaseObjectType.Item, "x", 11131, 0, 0, 1);

        var result = inv.RestoreCargoWithoutCreate(item, characterCoid: 1);
        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.IsNotNull(inv.FindByCoid(11131));
        Assert.IsFalse(result.Packets.OfType<CreateSimpleObjectPacket>().Any());
        Assert.IsTrue(result.Packets.OfType<InventoryAddItemResponsePacket>().Any(p => p.ItemCoid == 11131));
        Assert.IsTrue(result.Packets.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_RejectsDuplicateCoid()
    {
        var inv = new InventoryManager();
        var item = new CharacterInventoryItem(
            19194, CloneBaseObjectType.Item, "x", 11131, 0, 0, 1);
        Assert.IsTrue(inv.TryAdd(item));

        var result = inv.RestoreCargoWithoutCreate(item with { InventoryPositionX = 1 }, characterCoid: 1);
        Assert.AreEqual(0, result.AcceptedQuantity);
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_AssignsFreeSlot_NotOriginalCoords()
    {
        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
            1, CloneBaseObjectType.Item, "occ", 1, 0, 0, 1)));

        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "x", 11131, 0, 0, 2),
            characterCoid: 1);
        Assert.AreEqual(2, result.AcceptedQuantity);
        var restored = inv.FindByCoid(11131)!;
        Assert.IsFalse(restored.InventoryPositionX == 0 && restored.InventoryPositionY == 0);
        Assert.AreEqual(2, restored.Quantity);
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_PreservesMissionFlagAndCbid()
    {
        var inv = new InventoryManager();
        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(55, CloneBaseObjectType.QuestObject, "q", 9, 0, 0, 1, IsMissionItem: true),
            characterCoid: 1);
        Assert.AreEqual(1, result.AcceptedQuantity);
        var item = inv.FindByCoid(9)!;
        Assert.IsTrue(item.IsMissionItem);
        Assert.AreEqual(55, item.Cbid);
        Assert.AreEqual(CloneBaseObjectType.QuestObject, item.Type);
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_RejectsInvalidArgs()
    {
        var inv = new InventoryManager();
        Assert.AreEqual(0, inv.RestoreCargoWithoutCreate(null).AcceptedQuantity);
        Assert.AreEqual(0, inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(0, CloneBaseObjectType.Item, "x", 1, 0, 0, 1)).AcceptedQuantity);
        Assert.AreEqual(0, inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(1, CloneBaseObjectType.Item, "x", 0, 0, 0, 1)).AcceptedQuantity);
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_FullInventory_Fails()
    {
        var inv = new InventoryManager();
        long coid = 1000;
        for (byte y = 0; y < inv.PageCount; y++)
        {
            for (byte x = 0; x < inv.Width; x++)
                Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
                    1, CloneBaseObjectType.Item, "f", coid++, x, y, 1)));
        }

        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "x", 11131, 0, 0, 1));
        Assert.AreEqual(0, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(11131));
    }

    /// <summary>
    /// Buyback placement must use clonebase InvSize first-fit (client FUN_005713a0),
    /// not origin-only 1×1 linear slot scan.
    /// </summary>
    [TestMethod]
    public void RestoreCargoWithoutCreate_TwoByTwo_UsesFootprintFirstFit()
    {
        var (inv, _) = CreateWithSizes((10, 2, 2));
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
            10, CloneBaseObjectType.Item, "blocker", 1, 0, 0, 1)));

        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "buyback", 11131, 0, 0, 1),
            characterCoid: 1);

        Assert.AreEqual(1, result.AcceptedQuantity);
        var restored = inv.FindByCoid(11131)!;
        // Client packs 2×2 after a 2×2 at (0,0) → next origin (2,0), not (1,0).
        Assert.AreEqual((byte)2, restored.InventoryPositionX);
        Assert.AreEqual((byte)0, restored.InventoryPositionY);
        Assert.AreEqual(8, inv.GetOccupiedSlotCount());
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_TwoByTwo_FailsWhenOnlyOneByOneCellsFree()
    {
        var (inv, _) = CreateWithSizes((10, 2, 2), (1, 1, 1));
        // Checkerboard of 1×1 items leaves free cells but no contiguous 2×2.
        long coid = 100;
        for (byte y = 0; y < inv.PageCount; y++)
        {
            for (byte x = 0; x < inv.Width; x++)
            {
                if (((x + y) & 1) == 0)
                    Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(
                        1, CloneBaseObjectType.Item, "cell", coid++, x, y, 1)));
            }
        }

        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(10, CloneBaseObjectType.Item, "big", 11131, 0, 0, 1));

        Assert.AreEqual(0, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(11131));
        StringAssert.Contains(result.Message, "full");
    }

    [TestMethod]
    public void RestoreCargoWithoutCreate_RejectsZeroFootprintWhenCloneBasePresent()
    {
        var lookup = new FakeCloneBaseLookup();
        lookup.Register(77, CreateClone(77, 0, 0));
        var inv = new InventoryManager(cloneBases: lookup);

        var result = inv.RestoreCargoWithoutCreate(
            new CharacterInventoryItem(77, CloneBaseObjectType.Item, "zero", 11131, 0, 0, 1));

        Assert.AreEqual(0, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(11131));
        StringAssert.Contains(result.Message, "footprint");
    }

    private static (InventoryManager Inventory, FakeCloneBaseLookup Lookup) CreateWithSizes(
        params (int Cbid, byte SizeX, byte SizeY)[] sizes)
    {
        var lookup = new FakeCloneBaseLookup();
        foreach (var (cbid, sx, sy) in sizes)
            lookup.Register(cbid, CreateClone(cbid, sx, sy));

        return (new InventoryManager(cloneBases: lookup), lookup);
    }

    private static CloneBaseObject CreateClone(int cbid, byte sizeX, byte sizeY)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific { InvSizeX = sizeX, InvSizeY = sizeY };
        return clone;
    }
}
