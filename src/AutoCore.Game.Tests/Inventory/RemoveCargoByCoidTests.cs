using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;

[TestClass]
public class RemoveCargoByCoidTests
{
    [TestMethod]
    public void RemoveCargoByCoid_EmitsDestroyItemAndDestroyObject()
    {
        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "x", 11119, 0, 0, 1)));

        var result = inv.RemoveCargoByCoid(characterCoid: 1, itemCoid: 11119, itemGlobal: true);
        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(11119));
        Assert.IsTrue(result.Packets.OfType<InventoryDestroyItemPacket>().Any());
        Assert.IsTrue(result.Packets.OfType<DestroyObjectPacket>().Any());
        Assert.IsTrue(result.Packets.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void RemoveCargoByCoid_WithoutClientDestroy_OnlyCargoSendAll()
    {
        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "x", 11119, 0, 0, 1)));

        var result = inv.RemoveCargoByCoid(
            characterCoid: 1,
            itemCoid: 11119,
            itemGlobal: true,
            emitClientDestroy: false);
        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(11119));
        Assert.IsFalse(result.Packets.OfType<InventoryDestroyItemPacket>().Any());
        Assert.IsFalse(result.Packets.OfType<DestroyObjectPacket>().Any());
        Assert.IsTrue(result.Packets.OfType<InventoryCargoSendAllPacket>().Any());
    }

    [TestMethod]
    public void RemoveCargoByCoid_MissingItem_EmptyPackets()
    {
        var inv = new InventoryManager();
        var result = inv.RemoveCargoByCoid(1, itemCoid: 404, emitClientDestroy: false);
        Assert.AreEqual(0, result.AcceptedQuantity);
        Assert.AreEqual(0, result.Packets.Count);
    }

    [TestMethod]
    public void RemoveCargoByCoid_PicksExactCoid_NotSiblingStack()
    {
        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "a", 100, 0, 0, 2)));
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(19194, CloneBaseObjectType.Item, "b", 200, 1, 0, 3)));

        var result = inv.RemoveCargoByCoid(1, itemCoid: 200, emitClientDestroy: false);
        Assert.AreEqual(3, result.AcceptedQuantity);
        Assert.IsNull(inv.FindByCoid(200));
        Assert.IsNotNull(inv.FindByCoid(100));
        Assert.AreEqual(2, inv.FindByCoid(100)!.Quantity);
    }

    [TestMethod]
    public void RemoveCargoByCoid_DestroyPackets_CarryMatchingCoidAndQuantity()
    {
        var inv = new InventoryManager();
        Assert.IsTrue(inv.TryAdd(new CharacterInventoryItem(50, CloneBaseObjectType.Item, "s", 77, 0, 0, 4)));

        var result = inv.RemoveCargoByCoid(1, 77, itemGlobal: true, emitClientDestroy: true);
        var destroy = result.Packets.OfType<InventoryDestroyItemPacket>().Single();
        Assert.AreEqual(77L, destroy.ItemCoid);
        Assert.AreEqual(4, destroy.Quantity);
        Assert.IsTrue(destroy.Delete);

        var obj = result.Packets.OfType<DestroyObjectPacket>().Single();
        Assert.AreEqual(77L, obj.ObjectId.Coid);
        Assert.IsTrue(obj.ObjectId.Global);
    }
}
