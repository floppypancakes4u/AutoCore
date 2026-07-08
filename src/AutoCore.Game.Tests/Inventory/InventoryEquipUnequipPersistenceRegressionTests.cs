using AutoCore.Game.Constants;
using AutoCore.Game.Entities;
using AutoCore.Game.Inventory;
using AutoCore.Game.Packets.Sector;
using AutoCore.Game.Structures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoCore.Game.Tests.Inventory;

[TestClass]
public class InventoryEquipUnequipPersistenceRegressionTests
{
    [TestMethod]
    public void AddItem_WithCharacterCoid_PersistsCargoUpsert()
    {
        var persistence = new RecordingInventoryPersistence();
        var inventory = new InventoryManager(persistence);
        var service = new InventoryCommandService(
            new InventoryCatalog(() => new[]
            {
                new InventoryCatalogEntry(20, CloneBaseObjectType.Item, "Widget")
            }),
            new AlwaysSucceedItemCreator());

        var runtime = new FakeRuntime(inventory, characterCoid: 5001);
        var result = service.AddItem(runtime, new[] { "/addItem", "20" });

        Assert.IsNotNull(result.AddedItem, result.Message);
        Assert.AreEqual(1, persistence.Upserted.Count);
        Assert.AreEqual(5001, persistence.Upserted[0].CharacterCoid);
        Assert.AreEqual(20, persistence.Upserted[0].Item.Cbid);
        Assert.IsTrue(result.Packets.Any(p => p is InventoryCargoSendAllPacket));
    }

    [TestMethod]
    public void LoadItems_RestoresPersistedCargoIntoManager()
    {
        var inventory = new InventoryManager();
        inventory.SetCapacity(4, 2);
        inventory.LoadItems(new[]
        {
            new CharacterInventoryItem(8096, CloneBaseObjectType.Weapon, "Turret", 205, 1, 0, 1),
            new CharacterInventoryItem(100, CloneBaseObjectType.Armor, "Armor", 206, 0, 1, 1),
        });

        Assert.AreEqual(2, inventory.Items.Count);
        Assert.AreEqual(205, inventory.FindByCoid(205).Coid);
        Assert.AreEqual((byte)1, inventory.FindByCoid(205).InventoryPositionX);
        Assert.AreEqual(206, inventory.FindByCoid(206).Coid);
    }

    [TestMethod]
    public void HardpointEquipPacketOrder_Regression_NoDropResponse()
    {
        var vehicle = new Vehicle();
        vehicle.SetCoid(9001, true);
        var inventory = new InventoryManager();
        var packets = InventoryManager.BuildHardpointEquipPackets(
            inventory,
            vehicle,
            new TFID(205, true),
            null,
            sourceInventoryType: 1);

        Assert.IsTrue(packets[0] is InventoryEquipPacket);
        Assert.IsFalse(packets.Any(p => p is InventoryDropResponsePacket));
        Assert.IsTrue(packets.Any(p => p is InventoryCargoSendAllPacket));
    }

    [TestMethod]
    public void EquippedGrabPacketOrder_Regression_UnequipBeforeGrabResponse()
    {
        var packets = InventoryManager.BuildEquippedGrabPackets(
            new TFID(205, true),
            new TFID(9001, true),
            itemGlobal: true,
            inventoryType: 2);

        Assert.IsTrue(packets[0] is InventoryUnequipPacket);
        Assert.IsTrue(packets[1] is InventoryGrabResponsePacket);
    }

    private sealed class FakeRuntime : IInventoryRuntime
    {
        private long _next = 1001;

        public FakeRuntime(InventoryManager inventory, long characterCoid)
        {
            Inventory = inventory;
            CharacterCoid = characterCoid;
        }

        public bool CanAllocateItem => true;
        public InventoryManager Inventory { get; }
        public long CharacterCoid { get; }
        public long AllocateItemCoid() => _next++;
    }

    private sealed class AlwaysSucceedItemCreator : IInventoryItemCreator
    {
        public InventoryItemCreateResult Create(InventoryCatalogEntry entry, long coid, byte x, byte y)
        {
            return InventoryItemCreateResult.Success(
                new CreateSimpleObjectPacket
                {
                    CBID = entry.Cbid,
                    ObjectId = new(coid, true),
                    InventoryPositionX = x,
                    InventoryPositionY = y,
                    Quantity = 1,
                    IsInInventory = true
                },
                entry.DisplayName);
        }
    }

    private sealed class RecordingInventoryPersistence : IInventoryPersistence
    {
        public List<(long CharacterCoid, CharacterInventoryItem Item)> Upserted { get; } = new();

        public IReadOnlyList<CharacterInventoryItem> LoadCargo(long characterCoid) => Array.Empty<CharacterInventoryItem>();

        public void UpsertCargo(long characterCoid, CharacterInventoryItem item) =>
            Upserted.Add((characterCoid, item));

        public void MoveCargo(long characterCoid, CharacterInventoryItem item)
        {
        }

        public void DeleteCargo(long characterCoid, long itemCoid)
        {
        }

        public void EnsureSimpleObject(long itemCoid, byte type, int cbid, int faction = 0, int teamFaction = 0)
        {
        }

        public void SaveVehicleEquipment(long vehicleCoid, VehicleEquipmentSnapshot snapshot)
        {
        }

        public void SaveCharacterCargoCapacity(long characterCoid, int width, int pageCount)
        {
        }
    }
}
