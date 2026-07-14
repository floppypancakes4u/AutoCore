using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;

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
}
