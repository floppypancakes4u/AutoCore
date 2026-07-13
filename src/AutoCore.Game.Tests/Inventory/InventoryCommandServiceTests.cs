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
        Assert.IsInstanceOfType(result.Packets[1], typeof(InventoryAddItemResponsePacket));
        Assert.IsInstanceOfType(result.Packets[2], typeof(InventoryCargoSendAllPacket));

        var item = runtime.Inventory.Items.Single(i => i.Cbid == 20);
        Assert.AreEqual(1001, item.Coid);
        Assert.AreEqual(1, item.InventoryPositionX);
        Assert.AreEqual(0, item.InventoryPositionY);
        Assert.AreEqual(1, item.Quantity);
        Assert.AreSame(item, result.AddedItem);

        var cargoSnapshot = (InventoryCargoSendAllPacket)result.Packets[2];
        // Wire InventorySize is UI page count (height/13), not grid height.
        Assert.AreEqual(1, cargoSnapshot.InventorySize);
        Assert.AreEqual(1000, cargoSnapshot.Items[0].ItemCoid);
        Assert.AreEqual(0, cargoSnapshot.Items[0].PositionX);
        Assert.AreEqual(0, cargoSnapshot.Items[0].PositionY);
        Assert.AreEqual(1001, cargoSnapshot.Items[1].ItemCoid);
        Assert.AreEqual(1, cargoSnapshot.Items[1].PositionX);
        Assert.AreEqual(0, cargoSnapshot.Items[1].PositionY);
        Assert.AreEqual(-1, cargoSnapshot.Items[2].ItemCoid);

        var response = (InventoryAddItemResponsePacket)result.Packets[1];
        Assert.AreEqual(1001, response.ItemCoid);
        Assert.AreEqual((byte)1, response.InventoryPositionX);
        Assert.AreEqual((byte)0, response.InventoryPositionY);
        Assert.IsFalse(response.AddToExistingItem);
        Assert.AreEqual(1, response.Quantity);
        Assert.IsTrue(response.WasSuccessful);
    }

    [TestMethod]
    public void AddItem_WithQuantity_CreatesStackInSingleSlot()
    {
        var runtime = new FakeInventoryRuntime();
        var service = CreateService(
            new[] { Entry(20, CloneBaseObjectType.Item, "New Item") },
            new FakeInventoryItemCreator());

        var result = service.AddItem(runtime, new[] { "/addItem", "20", "2" });

        Assert.AreEqual("Added New Item (20) x2 to cargo slot 0,0.", result.Message);
        Assert.AreEqual(1, runtime.Inventory.Items.Count);

        var item = runtime.Inventory.Items.Single(i => i.Cbid == 20);
        Assert.AreEqual(2, item.Quantity);
        Assert.AreSame(item, result.AddedItem);

        var createPacket = (DummyCreatePacket)result.Packets[0];
        Assert.AreEqual(2, createPacket.Quantity);

        var response = (InventoryAddItemResponsePacket)result.Packets[1];
        Assert.IsFalse(response.AddToExistingItem);
        Assert.AreEqual(2, response.Quantity);
    }

    [TestMethod]
    public void AddItem_ByName_WithQuantity_CreatesStackInSingleSlot()
    {
        var runtime = new FakeInventoryRuntime();
        var service = CreateService(
            new[] { Entry(5123, CloneBaseObjectType.Item, "item_res_n_aliengoo_1") },
            new FakeInventoryItemCreator());

        var result = service.AddItem(runtime, new[] { "/addItem", "item_res_n_aliengoo_1", "3" });

        Assert.AreEqual("Added item_res_n_aliengoo_1 (5123) x3 to cargo slot 0,0.", result.Message);
        var item = runtime.Inventory.Items.Single(i => i.Cbid == 5123);
        Assert.AreEqual(3, item.Quantity);
    }

    [TestMethod]
    public void AddItem_WithQuantityThree_PutsFullQuantityOnCreatePacket()
    {
        var runtime = new FakeInventoryRuntime();
        var service = CreateService(
            new[] { Entry(20, CloneBaseObjectType.Item, "New Item") },
            new FakeInventoryItemCreator());

        var result = service.AddItem(runtime, new[] { "/addItem", "20", "3" });

        Assert.AreEqual(3, runtime.Inventory.Items.Single().Quantity);
        var createPacket = (DummyCreatePacket)result.Packets[0];
        Assert.AreEqual(3, createPacket.Quantity);
        var response = (InventoryAddItemResponsePacket)result.Packets[1];
        Assert.IsFalse(response.AddToExistingItem);
        Assert.AreEqual(3, response.Quantity);
    }

    [TestMethod]
    public void AddItem_ByName_IsCaseInsensitive()
    {
        var runtime = new FakeInventoryRuntime();
        var service = CreateService(
            new[] { Entry(5123, CloneBaseObjectType.Item, "item_res_n_aliengoo_1") },
            new FakeInventoryItemCreator());

        var result = service.AddItem(runtime, new[] { "/addItem", "ITEM_RES_N_ALIENGOO_1" });

        Assert.IsNotNull(result.AddedItem);
        Assert.AreEqual(5123, result.AddedItem.Cbid);
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

        Assert.AreEqual("Invalid addItem command. Usage: /addItem <cbid|name> [quantity].", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem" }).Message);
        Assert.AreEqual("Item name 'abc' was not found.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "abc" }).Message);
        Assert.AreEqual("Invalid quantity 'abc'. Quantity must be a positive number.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "20", "abc" }).Message);
        Assert.AreEqual("Quantity must be at least 1.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "20", "0" }).Message);
        Assert.AreEqual("Item name 'totally_unknown_item' was not found.", service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "totally_unknown_item", "2" }).Message);
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

        Assert.AreEqual(
            $"Cargo inventory is full ({InventoryManager.CargoSlotCount}/{InventoryManager.CargoSlotCount} slots used, {InventoryManager.CargoSlotCount} item(s) loaded). Try /cargoinfo or /clearcargo.",
            service.AddItem(fullRuntime, new[] { "/addItem", "20" }).Message);
    }

    [TestMethod]
    public void AddItem_RejectsAmbiguousName()
    {
        var service = CreateService(
            new[]
            {
                Entry(10, CloneBaseObjectType.Item, "duplicate_name"),
                Entry(11, CloneBaseObjectType.Weapon, "duplicate_name"),
            },
            new FakeInventoryItemCreator());

        Assert.AreEqual("Item name 'duplicate_name' is ambiguous (2 matches). Use the CBID instead.",
            service.AddItem(new FakeInventoryRuntime(), new[] { "/addItem", "duplicate_name" }).Message);
    }

    [TestMethod]
    public void ListItems_ReturnsCatalogPage()
    {
        var service = CreateService(
            Enumerable.Range(1, 3).Select(i => Entry(i, CloneBaseObjectType.Item, $"Item {i}")),
            new FakeInventoryItemCreator());

        var message = service.ListItems(new[] { "/listItems", "1" });

        StringAssert.Contains(message, "Item 1");
        StringAssert.Contains(message, "page 1/1");
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
        public long CharacterCoid => 5001;

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
