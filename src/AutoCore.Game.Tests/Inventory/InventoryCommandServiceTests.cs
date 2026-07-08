using AutoCore.Game.Constants;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets;
using AutoCore.Game.Packets.Sector;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryCommandServiceTests
{
    [TestMethod]
    public void AddItem_AllocatesFirstFreeCargoSlotRecordsItemAndReturnsCreateCargoSnapshotThenResponsePackets()
    {
        var runtime = new FakeInventoryRuntime();
        runtime.Inventory.TryAdd(new CharacterInventoryItem(10, CloneBaseObjectType.Item, "Existing", 1000, 0, 0, 1));

        var service = CreateService(
            new[] { Entry(20, CloneBaseObjectType.Item, "New Item") },
            new FakeInventoryItemCreator());

        var result = service.AddItem(runtime, new[] { "/addItem", "20" });

        Assert.AreEqual("Added New Item (20) to cargo slot 1,0.", result.Message);
        Assert.AreEqual(3, result.Packets.Count);
        Assert.IsInstanceOfType(result.Packets[0], typeof(DummyCreatePacket));
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryCargoSendAllPacket));
        Assert.IsInstanceOfType(result.Packets[2], typeof(InventoryAddItemResponsePacket));

        var item = runtime.Inventory.Items.Single(i => i.Cbid == 20);
        Assert.AreEqual(1001, item.Coid);
        Assert.AreEqual(1, item.InventoryPositionX);
        Assert.AreEqual(0, item.InventoryPositionY);
        Assert.AreEqual(1, item.Quantity);
        Assert.AreSame(item, result.AddedItem);

        var cargoSnapshot = (InventoryCargoSendAllPacket)result.Packets[1];
        Assert.AreEqual(InventoryManager.CargoPageCount, cargoSnapshot.InventorySize);
        Assert.AreEqual(1000, cargoSnapshot.Items[0].ItemCoid);
        Assert.AreEqual(0, cargoSnapshot.Items[0].PositionX);
        Assert.AreEqual(0, cargoSnapshot.Items[0].PositionY);
        Assert.AreEqual(1001, cargoSnapshot.Items[1].ItemCoid);
        Assert.AreEqual(1, cargoSnapshot.Items[1].PositionX);
        Assert.AreEqual(0, cargoSnapshot.Items[1].PositionY);
        Assert.AreEqual(-1, cargoSnapshot.Items[2].ItemCoid);

        var response = (InventoryAddItemResponsePacket)result.Packets[2];
        Assert.AreEqual(1001, response.ItemCoid);
        Assert.AreEqual((byte)1, response.InventoryPositionX);
        Assert.AreEqual((byte)0, response.InventoryPositionY);
        Assert.IsFalse(response.AddToExistingItem);
        Assert.AreEqual(1, response.Quantity);
        Assert.IsTrue(response.WasSuccessful);
    }

    [TestMethod]
    public void AddItem_RejectsUnknownNonInventoryUnsupportedNoMapAndFullCargo()
    {
        var service = CreateService(
            new[]
            {
                Entry(20, CloneBaseObjectType.Item, "New Item"),
                Entry(30, CloneBaseObjectType.Creature, "Creature"),
                Entry(40, CloneBaseObjectType.Gadget, "Unsupported Gadget"),
            },
            new FakeInventoryItemCreator(unsupportedCbids: new[] { 40 }));

        Assert.AreEqual("Invalid addItem command. Usage: /addItem <cbid>.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem" }).Message);
        Assert.AreEqual("Invalid item id 'abc'. Item id must be a CBID number.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "abc" }).Message);
        Assert.AreEqual("Item CBID 999 was not found.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "999" }).Message);
        Assert.AreEqual("CBID 30 (Creature) is not an inventory item.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "30" }).Message);
        Assert.AreEqual("Cannot add item: character or map is not available.", service.AddItem(new FakeInventoryRuntime(canAllocate: false), new[] { "/addItem", "20" }).Message);
        Assert.AreEqual("Cannot add CBID 40: item type Gadget is not supported by the server create factory yet.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "40" }).Message);

        var fullRuntime = new FakeInventoryRuntime();
        for (var i = 0; i < InventoryManager.CargoSlotCount; i++)
        {
            var x = (byte)(i % InventoryManager.CargoWidth);
            var y = (byte)(i / InventoryManager.CargoWidth);
            fullRuntime.Inventory.TryAdd(new CharacterInventoryItem(10000 + i, CloneBaseObjectType.Item, $"Item {i}", 20000 + i, x, y, 1));
        }

        Assert.AreEqual("Cargo inventory is full (312/312).", service.AddItem(fullRuntime, new[] { "/addItem", "20" }).Message);
    }

    private static InventoryCommandService CreateService(IEnumerable<InventoryCatalogEntry> entries, IInventoryItemCreator creator)
    {
        return new InventoryCommandService(new InventoryCatalog(() => entries), creator);
    }

    private static InventoryCatalogEntry Entry(int cbid, CloneBaseObjectType type, string name)
    {
        return new InventoryCatalogEntry(cbid, type, name);
    }

    private sealed class FakeInventoryRuntime : IInventoryRuntime
    {
        private long _nextCoid = 1001;

        public FakeInventoryRuntime(bool canAllocate = true)
        {
            CanAllocateItem = canAllocate;
        }

        public bool CanAllocateItem { get; }
        public InventoryManager Inventory { get; } = new();

        public long AllocateItemCoid()
        {
            return _nextCoid++;
        }
    }

    private sealed class FakeInventoryItemCreator : IInventoryItemCreator
    {
        private readonly HashSet<int> _unsupportedCbids;

        public FakeInventoryItemCreator(IEnumerable<int>? unsupportedCbids = null)
        {
            _unsupportedCbids = unsupportedCbids?.ToHashSet() ?? new HashSet<int>();
        }

        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            if (_unsupportedCbids.Contains(entry.Cbid))
                return InventoryItemCreateResult.Unsupported($"item type {entry.Type} is not supported by the server create factory yet.");

            return InventoryItemCreateResult.Success(new DummyCreatePacket(), entry.DisplayName);
        }
    }

    private sealed class DummyCreatePacket : CreateSimpleObjectPacket
    {
        public override void Write(BinaryWriter writer)
        {
        }
    }
}
