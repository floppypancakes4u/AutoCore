using System.Runtime.CompilerServices;
using AutoCore.Game.CloneBases;
using AutoCore.Game.CloneBases.Specifics;
using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Tests.Inventory.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryAcquisitionStackTests
{
    [TestMethod]
    public void AddItem_FillsExistingStackWhenCargoHasNoFreeSlot()
    {
        var persistence = new RecordingInventoryPersistence();
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 10));
        var inventory = new InventoryManager(persistence, clones);
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 100, 0, 0, 7));

        var result = inventory.AddItem(Entry(10), new TestItemCreator(), coid: 200, characterCoid: 5001, quantity: 3);

        Assert.AreEqual(3, result.AcceptedQuantity);
        Assert.AreEqual(0, result.RemainingQuantity);
        Assert.AreEqual(10, inventory.FindByCoid(100).Quantity);
        Assert.AreEqual(1, inventory.Items.Count);
        Assert.AreEqual(2, result.Packets.Count);
        var update = (InventoryAddItemResponsePacket)result.Packets[0];
        Assert.AreEqual(100, update.ItemCoid);
        Assert.IsTrue(update.AddToExistingItem);
        Assert.AreEqual(10, update.Quantity);
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
        Assert.AreEqual(1, persistence.Upserted.Count);
        Assert.AreEqual(100, persistence.Upserted[0].Item.Coid);
    }

    [TestMethod]
    public void AddItem_FillsStacksBySlotThenCreatesCappedOverflowStacks()
    {
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 5));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(4, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 101, 1, 0, 4));
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 102, 3, 0, 3));
        var coids = new Queue<long>(new[] { 202L, 203L });

        var result = inventory.AddItem(Entry(10), new TestItemCreator(), coid: 201, quantity: 10,
            allocateAdditionalCoid: () => coids.Dequeue());

        Assert.AreEqual(10, result.AcceptedQuantity);
        Assert.AreEqual(0, result.RemainingQuantity);
        Assert.AreEqual(5, inventory.FindByCoid(101).Quantity, "slot 1 is filled before slot 3");
        Assert.AreEqual(5, inventory.FindByCoid(102).Quantity);
        Assert.AreEqual(5, inventory.FindByCoid(201).Quantity);
        Assert.AreEqual(2, inventory.FindByCoid(202).Quantity);
        CollectionAssert.AreEqual(new long[] { 101, 102, 201, 202 },
            result.Packets.OfType<InventoryAddItemResponsePacket>().Select(p => p.ItemCoid).ToArray());
        Assert.AreEqual(7, result.Packets.Count, "update/create pairs followed by one cargo snapshot");
    }

    [TestMethod]
    public void AddItem_AcceptsOnlyCapacityAndLeavesRemainder()
    {
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 5));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 100, 0, 0, 4));

        var result = inventory.AddItem(Entry(10), new TestItemCreator(), coid: 200, quantity: 4);

        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.AreEqual(3, result.RemainingQuantity);
        Assert.AreEqual(5, inventory.FindByCoid(100).Quantity);
        Assert.AreEqual(1, result.Packets.OfType<InventoryAddItemResponsePacket>().Count());
    }

    [TestMethod]
    public void PickupWorldItem_MergesIntoExistingStackWithoutCreatingAnotherClientObject()
    {
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 5));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 100, 0, 0, 4));

        var result = inventory.PickupWorldItem(10, CloneBaseObjectType.Item, "Parts", 200,
            new TestItemCreator(), characterCoid: 5001);

        Assert.IsNotNull(result.AddedItem, "A merged stack is a successful world claim.");
        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.AreEqual(5, inventory.FindByCoid(100).Quantity);
        Assert.AreEqual(2, result.Packets.Count);
        var update = (InventoryAddItemResponsePacket)result.Packets[0];
        Assert.AreEqual(100, update.ItemCoid);
        Assert.IsTrue(update.AddToExistingItem);
        Assert.AreEqual(5, update.Quantity);
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
    }

    [TestMethod]
    public void AddItem_NonStackableItemUsesSeparateSlots()
    {
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 1));
        var inventory = new InventoryManager(cloneBases: clones);
        inventory.SetCapacity(2, 1);
        var coids = new Queue<long>(new[] { 201L });

        var result = inventory.AddItem(Entry(10), new TestItemCreator(), 200, quantity: 2,
            allocateAdditionalCoid: () => coids.Dequeue());

        Assert.AreEqual(2, result.AcceptedQuantity);
        Assert.AreEqual(2, inventory.Items.Count);
        CollectionAssert.AreEqual(new[] { 1, 1 }, inventory.Items.OrderBy(i => i.Coid).Select(i => i.Quantity).ToArray());
        Assert.IsTrue(result.Packets.OfType<InventoryAddItemResponsePacket>().All(packet => !packet.AddToExistingItem));
    }

    [TestMethod]
    public void GrantMissionCargoItem_MergesAndPreservesMissionCargoMarker()
    {
        var persistence = new RecordingInventoryPersistence();
        var clones = new FakeCloneBaseLookup();
        clones.Register(10, StackableClone(10, 5));
        var inventory = new InventoryManager(persistence, clones);
        inventory.SetCapacity(1, 1);
        inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Parts", 100, 0, 0, 4, IsMissionItem: true));

        var result = inventory.GrantMissionCargoItem(10, CloneBaseObjectType.Item, "Parts", 200, 5001,
            itemCreator: new TestItemCreator());

        Assert.AreEqual(1, result.AcceptedQuantity);
        Assert.AreEqual(5, inventory.FindByCoid(100).Quantity);
        Assert.IsTrue(inventory.FindByCoid(100).IsMissionItem);
        Assert.AreEqual(100, ((InventoryAddItemResponsePacket)result.Packets[0]).ItemCoid);
        Assert.IsTrue(((InventoryAddItemResponsePacket)result.Packets[0]).AddToExistingItem);
        Assert.AreEqual(100, persistence.Upserted.Single().Item.Coid);
    }

    private static InventoryCatalogEntry Entry(int cbid) => new(cbid, CloneBaseObjectType.Item, "Parts");

    private static CloneBaseObject StackableClone(int cbid, ushort stackSize)
    {
        var clone = (CloneBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(CloneBaseObject));
        clone.CloneBaseSpecific = new CloneBaseSpecific { Type = (int)CloneBaseObjectType.Item, CloneBaseId = cbid };
        clone.SimpleObjectSpecific = new SimpleObjectSpecific
        {
            StackSize = stackSize,
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
